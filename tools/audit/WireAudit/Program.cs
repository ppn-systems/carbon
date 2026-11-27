// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Carbon.Tools.WireAudit;

/// <summary>
/// Mechanical wire-protocol audit. Compiles every production source file (contracts, engine, graphics, server,
/// desktop, wasm) into one Roslyn compilation and reports, for every packet and every <c>[SerializeOrder]</c>
/// field, which side writes it and which side reads it; for every opcode, which side sends it and which side
/// handles it; and which contract enum values and types nobody outside <c>contracts/</c> references.
/// <para>
/// A reference inside contracts/, engine/ or graphics/ is attributed to the side(s) whose code calls the enclosing
/// member, transitively, so a mapper in engine/ that fills a packet counts for whoever calls the mapper.
/// </para>
/// </summary>
internal static class Program
{
    private static readonly string[] s_sourceRoots = ["contracts", "engine", "graphics", "server", "desktop", "wasm"];
    private static readonly string[] s_receiveMethods = ["On", "RequestAsync", "StreamAsync"];
    private static readonly string[] s_collectionWriters = ["Add", "AddRange", "Insert", "Fill", "CopyFrom"];
    private static readonly string[] s_resetMembers = ["ResetForPool", "Reset", "Clear", "Dispose"];

    private static int Main(string[] args)
    {
        string root = args.Length > 0 ? Path.GetFullPath(args[0]) : FindRepoRoot();
        Analyzer analyzer = new(root);
        analyzer.Run();
        Console.Out.Write(analyzer.Render());
        return 0;
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(Environment.CurrentDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "contracts")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("repository root (contracts/) not found");
    }

    internal enum Side
    {
        Shared,
        Server,
        Client,
    }

    internal sealed class FieldUse
    {
        public FieldUse(IPropertySymbol property, int order)
        {
            this.Property = property;
            this.Order = order;
        }

        public IPropertySymbol Property { get; }

        public int Order { get; }

        public HashSet<Side> Writers { get; } = [];

        public HashSet<Side> Readers { get; } = [];
    }

    internal sealed class PacketInfo
    {
        public PacketInfo(INamedTypeSymbol type, string file)
        {
            this.Type = type;
            this.File = file;
        }

        public INamedTypeSymbol Type { get; }

        public string File { get; }

        public ushort? OpCode { get; set; }

        public string OpCodeName { get; set; } = string.Empty;

        public List<FieldUse> Fields { get; } = [];

        public HashSet<Side> Constructs { get; } = [];

        /// <summary>Gets the members (outside contracts/) that fill or rent this packet, to test reachability.</summary>
        public HashSet<ISymbol> SenderMembers { get; } = new(SymbolEqualityComparer.Default);

        public HashSet<Side> Handles { get; } = [];

        public bool IsPacket => this.OpCode.HasValue;

        public string Direction =>
            this.Type.ContainingNamespace.ToDisplayString().EndsWith(".C2S", StringComparison.Ordinal) ? "C2S"
            : this.Type.ContainingNamespace.ToDisplayString().EndsWith(".S2C", StringComparison.Ordinal) ? "S2C"
            : "shared";
    }

    internal sealed class Analyzer
    {
        private readonly string _root;
        private readonly Dictionary<ISymbol, PacketInfo> _packets = new(SymbolEqualityComparer.Default);
        private readonly Dictionary<ISymbol, FieldUse> _fields = new(SymbolEqualityComparer.Default);
        private readonly Dictionary<ISymbol, HashSet<ISymbol>> _callers = new(SymbolEqualityComparer.Default);
        private readonly Dictionary<ISymbol, HashSet<Side>> _sideMemo = new(SymbolEqualityComparer.Default);
        private readonly Dictionary<ISymbol, bool> _reachMemo = new(SymbolEqualityComparer.Default);
        private readonly Dictionary<ISymbol, Dictionary<Side, int>> _contractTypeRefs = new(SymbolEqualityComparer.Default);
        private readonly Dictionary<ISymbol, HashSet<Side>> _enumMemberRefs = new(SymbolEqualityComparer.Default);
        private readonly List<(string Name, ushort Value)> _opCodeConstants = [];
        private CSharpCompilation? _compilation;

        public Analyzer(string root) => _root = root;

        public void Run()
        {
            _compilation = this.Compile();
            foreach (SyntaxTree tree in _compilation.SyntaxTrees)
            {
                this.CollectDeclarations(tree);
            }

            foreach (SyntaxTree tree in _compilation.SyntaxTrees)
            {
                this.CollectCallers(tree);
            }

            foreach (SyntaxTree tree in _compilation.SyntaxTrees)
            {
                this.CollectUses(tree);
            }
        }

        public string Render()
        {
            StringBuilder sb = new();
            CultureInfo inv = CultureInfo.InvariantCulture;
            List<PacketInfo> packets = [.. _packets.Values.Where(static p => p.IsPacket).OrderBy(static p => p.OpCode)];

            sb.AppendLine("## Opcodes");
            sb.AppendLine();
            sb.AppendLine("| Opcode | Constant | Packet | Dir | Sent by | Handled by | Fields |");
            sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- | ---: |");
            foreach ((string name, ushort value) in _opCodeConstants.OrderBy(static c => c.Value))
            {
                PacketInfo? p = packets.FirstOrDefault(x => x.OpCode == value);
                sb.Append(inv, $"| `0x{value:X4}` | `{name}` | ");
                if (p is null)
                {
                    sb.AppendLine("— (no packet) | | | | |");
                    continue;
                }

                string sent = Sides(p.Constructs);
                if (p.SenderMembers.Count > 0 && !p.SenderMembers.Any(this.IsReachable))
                {
                    sent += " (only from uncalled " + string.Join(", ", p.SenderMembers.Select(static m => $"`{m.ContainingType.Name}.{m.Name}`").Distinct().Order(StringComparer.Ordinal)) + ")";
                }

                sb.Append(inv, $"`{p.Type.Name}` | {p.Direction} | {sent} | {Sides(p.Handles)} | {p.Fields.Count} |");
                sb.AppendLine();
            }

            sb.AppendLine();
            sb.AppendLine("## Fields");
            foreach (PacketInfo p in _packets.Values.OrderBy(static p => p.OpCode ?? ushort.MaxValue).ThenBy(static p => p.Type.Name, StringComparer.Ordinal))
            {
                sb.AppendLine();
                string head = p.IsPacket ? string.Create(inv, $"`0x{p.OpCode:X4}` ") : "DTO ";
                sb.AppendLine(inv, $"### {head}`{p.Type.Name}` ({p.Direction}, `{p.File}`)");
                sb.AppendLine();
                sb.AppendLine("| # | Field | Type | Written by | Read by | Flag |");
                sb.AppendLine("| ---: | :--- | :--- | :--- | :--- | :--- |");
                foreach (FieldUse f in p.Fields.OrderBy(static f => f.Order))
                {
                    string flag = Flag(p, f);
                    sb.AppendLine(inv, $"| {f.Order} | `{f.Property.Name}` | `{f.Property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}` | {Sides(f.Writers)} | {Sides(f.Readers)} | {flag} |");
                }
            }

            sb.AppendLine();
            sb.AppendLine("## Contract enum values never referenced outside contracts/");
            sb.AppendLine();
            foreach (KeyValuePair<ISymbol, HashSet<Side>> e in _enumMemberRefs
                         .Where(static e => e.Value.Count == 0)
                         .OrderBy(static e => e.Key.ContainingType.Name, StringComparer.Ordinal)
                         .ThenBy(static e => e.Key.Name, StringComparer.Ordinal))
            {
                sb.AppendLine(inv, $"- `{e.Key.ContainingType.Name}.{e.Key.Name}`");
            }

            sb.AppendLine();
            sb.AppendLine("## Contract types and their references outside contracts/");
            sb.AppendLine();
            sb.AppendLine("| Type | Server refs | Client refs | Engine/graphics refs |");
            sb.AppendLine("| :--- | ---: | ---: | ---: |");
            foreach (KeyValuePair<ISymbol, Dictionary<Side, int>> t in _contractTypeRefs.OrderBy(static t => t.Key.ToDisplayString(), StringComparer.Ordinal))
            {
                int Get(Side s) => t.Value.TryGetValue(s, out int n) ? n : 0;
                sb.AppendLine(inv, $"| `{t.Key.ToDisplayString()}` | {Get(Side.Server)} | {Get(Side.Client)} | {Get(Side.Shared)} |");
            }

            return sb.ToString();
        }

        private static string Flag(PacketInfo p, FieldUse f)
        {
            List<string> flags = [];
            if (f.Writers.Count == 0)
            {
                flags.Add("**never written**");
            }

            if (f.Readers.Count == 0)
            {
                flags.Add("**never read**");
            }

            Side? reader = p.Direction switch
            {
                "C2S" => Side.Server,
                "S2C" => Side.Client,
                _ => null,
            };
            if (reader is { } r && f.Readers.Count > 0 && !f.Readers.Contains(r))
            {
                flags.Add(string.Create(CultureInfo.InvariantCulture, $"not read by {r.ToString().ToLowerInvariant()}"));
            }

            if (f.Property.Type.SpecialType == SpecialType.System_String)
            {
                flags.Add("string");
            }

            return string.Join(", ", flags);
        }

        private static string Sides(HashSet<Side> sides) =>
            sides.Count == 0 ? "—" : string.Join(" + ", sides.Where(static s => s != Side.Shared).OrderBy(static s => s).Select(static s => s.ToString().ToLowerInvariant()));

        private CSharpCompilation Compile()
        {
            List<SyntaxTree> trees = [];
            CSharpParseOptions options = new(LanguageVersion.Preview);
            foreach (string dir in s_sourceRoots)
            {
                string full = Path.Combine(_root, dir);
                if (!Directory.Exists(full))
                {
                    continue;
                }

                foreach (string file in Directory.EnumerateFiles(full, "*.cs", SearchOption.AllDirectories))
                {
                    string rel = Path.GetRelativePath(_root, file).Replace('\\', '/');
                    if (rel.Contains("/obj/", StringComparison.Ordinal) || rel.Contains("/bin/", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(file), options, rel));
                }
            }

            Dictionary<string, MetadataReference> refs = new(StringComparer.OrdinalIgnoreCase);
            string runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            AddReferences(refs, Directory.EnumerateFiles(runtimeDir, "*.dll"));
            string artifacts = Path.Combine(_root, "artifacts", "bin");
            if (Directory.Exists(artifacts))
            {
                AddReferences(refs, Directory.EnumerateFiles(artifacts, "*.dll", SearchOption.AllDirectories)
                    .Where(static f => !Path.GetFileName(f).StartsWith("Carbon.", StringComparison.Ordinal)));
            }

            return CSharpCompilation.Create(
                "WireAudit",
                trees,
                refs.Values,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable));
        }

        private static void AddReferences(Dictionary<string, MetadataReference> refs, IEnumerable<string> files)
        {
            foreach (string file in files)
            {
                string name = Path.GetFileName(file);
                if (refs.ContainsKey(name))
                {
                    continue;
                }

                try
                {
                    using FileStream stream = File.OpenRead(file);
                    using System.Reflection.PortableExecutable.PEReader pe = new(stream);
                    if (pe.HasMetadata)
                    {
                        refs[name] = MetadataReference.CreateFromFile(file);
                    }
                }
                catch (BadImageFormatException)
                {
                    // Native library next to the managed ones.
                }
            }
        }

        private static Side SideOf(string path) =>
            path.StartsWith("server/", StringComparison.Ordinal) ? Side.Server
            : path.StartsWith("desktop/", StringComparison.Ordinal) || path.StartsWith("wasm/", StringComparison.Ordinal) || path.StartsWith("graphics/", StringComparison.Ordinal) ? Side.Client
            : Side.Shared;

        private static bool IsContracts(string path) => path.StartsWith("contracts/", StringComparison.Ordinal);

        private void CollectDeclarations(SyntaxTree tree)
        {
            if (!IsContracts(tree.FilePath))
            {
                return;
            }

            SemanticModel model = _compilation!.GetSemanticModel(tree);
            foreach (BaseTypeDeclarationSyntax decl in tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(decl) is not INamedTypeSymbol type)
                {
                    continue;
                }

                _contractTypeRefs[type] = [];
                if (type.TypeKind == TypeKind.Enum)
                {
                    foreach (IFieldSymbol member in type.GetMembers().OfType<IFieldSymbol>())
                    {
                        _enumMemberRefs[member] = [];
                    }

                    continue;
                }

                if (type.IsStatic && type.Name.EndsWith("OpCode", StringComparison.Ordinal))
                {
                    foreach (IFieldSymbol c in type.GetMembers().OfType<IFieldSymbol>().Where(static f => f.IsConst && f.ConstantValue is ushort))
                    {
                        _opCodeConstants.Add(($"{type.Name}.{c.Name}", (ushort)c.ConstantValue!));
                    }
                }

                if (!type.GetAttributes().Any(static a => a.AttributeClass?.Name == "SerializePackableAttribute"))
                {
                    continue;
                }

                if (_packets.ContainsKey(type))
                {
                    continue;
                }

                PacketInfo info = new(type, tree.FilePath);
                _packets[type] = info;
                foreach (IPropertySymbol prop in type.GetMembers().OfType<IPropertySymbol>())
                {
                    AttributeData? order = prop.GetAttributes().FirstOrDefault(static a => a.AttributeClass?.Name == "SerializeOrderAttribute");
                    if (order is { ConstructorArguments.Length: > 0 } && order.ConstructorArguments[0].Value is int n)
                    {
                        FieldUse use = new(prop, n);
                        info.Fields.Add(use);
                        _fields[prop] = use;
                    }

                    if (prop is { IsStatic: true, Name: "StaticOpCode" })
                    {
                        info.OpCode = ResolveOpCode(model, prop);
                    }
                }
            }
        }

        private static ushort? ResolveOpCode(SemanticModel model, IPropertySymbol prop)
        {
            foreach (SyntaxReference r in prop.DeclaringSyntaxReferences)
            {
                if (r.GetSyntax() is PropertyDeclarationSyntax { ExpressionBody.Expression: { } expr })
                {
                    SemanticModel m = model.Compilation.GetSemanticModel(r.SyntaxTree);
                    Optional<object?> value = m.GetConstantValue(expr);
                    if (value.HasValue && value.Value is ushort u)
                    {
                        return u;
                    }

                    if (expr is CastExpressionSyntax cast && m.GetConstantValue(cast.Expression) is { HasValue: true, Value: { } v })
                    {
                        return Convert.ToUInt16(v, CultureInfo.InvariantCulture);
                    }
                }
            }

            return null;
        }

        private void CollectCallers(SyntaxTree tree)
        {
            SemanticModel model = _compilation!.GetSemanticModel(tree);
            foreach (SyntaxNode node in tree.GetRoot().DescendantNodes())
            {
                if (node is not (SimpleNameSyntax or BaseObjectCreationExpressionSyntax))
                {
                    continue;
                }

                ISymbol? callee = model.GetSymbolInfo(node).Symbol ?? model.GetSymbolInfo(node).CandidateSymbols.FirstOrDefault();
                if (callee is not (IMethodSymbol or IPropertySymbol) || callee.Locations.All(static l => !l.IsInSource))
                {
                    continue;
                }

                ISymbol? caller = EnclosingMember(model, node);
                if (caller is null)
                {
                    continue;
                }

                this.AddCaller(callee.OriginalDefinition, caller);
            }

            // A call through an interface or base member reaches every implementation.
            foreach (TypeDeclarationSyntax decl in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(decl) is not INamedTypeSymbol type)
                {
                    continue;
                }

                foreach (ISymbol member in type.GetMembers())
                {
                    foreach (INamedTypeSymbol iface in type.AllInterfaces)
                    {
                        foreach (ISymbol im in iface.GetMembers())
                        {
                            if (SymbolEqualityComparer.Default.Equals(type.FindImplementationForInterfaceMember(im), member))
                            {
                                this.AddCaller(member, im.OriginalDefinition);
                            }
                        }
                    }

                    ISymbol? overridden = member switch
                    {
                        IMethodSymbol m => m.OverriddenMethod,
                        IPropertySymbol p => p.OverriddenProperty,
                        _ => null,
                    };
                    if (overridden is not null)
                    {
                        this.AddCaller(member, overridden.OriginalDefinition);
                    }
                }
            }
        }

        private void AddCaller(ISymbol callee, ISymbol caller)
        {
            if (!_callers.TryGetValue(callee, out HashSet<ISymbol>? set))
            {
                set = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
                _callers[callee] = set;
            }

            set.Add(caller);
        }

        private static ISymbol? EnclosingMember(SemanticModel model, SyntaxNode node)
        {
            for (SyntaxNode? n = node.Parent; n is not null; n = n.Parent)
            {
                switch (n)
                {
                    case BaseMethodDeclarationSyntax or BasePropertyDeclarationSyntax:
                        return model.GetDeclaredSymbol(n);
                    case VariableDeclaratorSyntax v when v.Parent?.Parent is BaseFieldDeclarationSyntax:
                        return model.GetDeclaredSymbol(v);
                    case BaseTypeDeclarationSyntax t:
                        return model.GetDeclaredSymbol(t);
                    default:
                        break;
                }
            }

            return null;
        }

        /// <summary>The sides whose code reaches <paramref name="member"/>, following callers through shared code.</summary>
        private HashSet<Side> SidesOf(ISymbol member)
        {
            if (_sideMemo.TryGetValue(member, out HashSet<Side>? memo))
            {
                return memo;
            }

            HashSet<Side> sides = [];
            _sideMemo[member] = sides; // cycle guard
            Location? loc = member.Locations.FirstOrDefault(static l => l.IsInSource);
            Side own = loc is null ? Side.Shared : SideOf(loc.SourceTree!.FilePath);
            if (own != Side.Shared)
            {
                sides.Add(own);
                return sides;
            }

            if (_callers.TryGetValue(member, out HashSet<ISymbol>? callers))
            {
                foreach (ISymbol c in callers)
                {
                    sides.UnionWith(this.SidesOf(c));
                }
            }

            if (member.ContainingType is { } containing && member is not INamedTypeSymbol)
            {
                // Field initializers and type-level code run for whoever reaches the type.
                if (member is IFieldSymbol)
                {
                    sides.UnionWith(this.SidesOf(containing));
                }
            }

            return sides;
        }

        /// <summary>
        /// Whether production code can reach <paramref name="member"/>: it is an entry point (a packet handler, a
        /// <c>Main</c>, a constructor, or a member the framework calls because it overrides or implements a library
        /// member), or some reachable member references it.
        /// </summary>
        private bool IsReachable(ISymbol member)
        {
            if (_reachMemo.TryGetValue(member, out bool known))
            {
                return known;
            }

            _reachMemo[member] = false; // cycle guard
            bool entry = member switch
            {
                IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor } => true,
                IMethodSymbol { Name: "Main" } => true,
                IMethodSymbol m when m.GetAttributes().Any(static a => a.AttributeClass?.Name == "PacketOpcodeAttribute") => true,
                IMethodSymbol { OverriddenMethod: { } o } when !o.Locations.Any(static l => l.IsInSource) => true,
                IPropertySymbol { OverriddenProperty: { } o } when !o.Locations.Any(static l => l.IsInSource) => true,
                INamedTypeSymbol or IFieldSymbol => true,
                _ => ImplementsLibraryMember(member),
            };

            bool reached = entry || (_callers.TryGetValue(member, out HashSet<ISymbol>? callers) && callers.Any(this.IsReachable));
            _reachMemo[member] = reached;
            return reached;
        }

        private static bool ImplementsLibraryMember(ISymbol member)
        {
            INamedTypeSymbol? type = member.ContainingType;
            if (type is null)
            {
                return false;
            }

            foreach (INamedTypeSymbol iface in type.AllInterfaces.Where(static i => !i.Locations.Any(static l => l.IsInSource)))
            {
                foreach (ISymbol im in iface.GetMembers())
                {
                    if (SymbolEqualityComparer.Default.Equals(type.FindImplementationForInterfaceMember(im), member))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void CollectUses(SyntaxTree tree)
        {
            SemanticModel model = _compilation!.GetSemanticModel(tree);
            string path = tree.FilePath;
            Side fileSide = SideOf(path);
            bool inContracts = IsContracts(path);

            foreach (SyntaxNode node in tree.GetRoot().DescendantNodes())
            {
                if (node is TypeSyntax typeSyntax && node is not QualifiedNameSyntax && !inContracts)
                {
                    this.CountTypeRef(model, typeSyntax, fileSide);
                }

                switch (node)
                {
                    case AttributeSyntax attr when fileSide == Side.Server && attr.Name.ToString() is "PacketOpcode" or "PacketOpcodeAttribute":
                        this.RecordServerHandler(model, attr);
                        break;
                    case GenericNameSyntax generic:
                        this.RecordGenericUse(model, generic, fileSide);
                        break;
                    case BaseObjectCreationExpressionSyntax creation when model.GetTypeInfo(creation).Type is { } created
                                                                          && _packets.TryGetValue(created.OriginalDefinition, out PacketInfo? made):
                        this.Constructs(made, model, creation);
                        break;
                    default:
                        break;
                }

                if (node is not SimpleNameSyntax name)
                {
                    continue;
                }

                ISymbol? symbol = model.GetSymbolInfo(name).Symbol;
                if (symbol is IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum } enumMember && !inContracts
                    && _enumMemberRefs.TryGetValue(enumMember.OriginalDefinition, out HashSet<Side>? enumSides))
                {
                    enumSides.Add(fileSide);
                }

                if (symbol is not IPropertySymbol prop || !_fields.TryGetValue(prop.OriginalDefinition, out FieldUse? use))
                {
                    continue;
                }

                ISymbol? enclosing = EnclosingMember(model, name);
                if (inContracts && enclosing is not null && s_resetMembers.Contains(enclosing.Name, StringComparer.Ordinal))
                {
                    continue;
                }

                (bool read, bool write) = Classify(name);
                HashSet<Side> sides = fileSide != Side.Shared ? [fileSide] : enclosing is null ? [] : this.SidesOf(enclosing);
                if (write)
                {
                    use.Writers.UnionWith(sides);
                    if (_packets.TryGetValue(prop.ContainingType.OriginalDefinition, out PacketInfo? owner))
                    {
                        owner.Constructs.UnionWith(sides);
                        if (fileSide != Side.Shared && enclosing is not null)
                        {
                            owner.SenderMembers.Add(enclosing);
                        }
                    }
                }

                if (read)
                {
                    use.Readers.UnionWith(sides);
                }
            }
        }

        private void Constructs(PacketInfo packet, SemanticModel model, SyntaxNode at)
        {
            Side side = SideOf(at.SyntaxTree.FilePath);
            if (side != Side.Shared)
            {
                packet.Constructs.Add(side);
                if (EnclosingMember(model, at) is { } sender)
                {
                    packet.SenderMembers.Add(sender);
                }
            }
            else if (EnclosingMember(model, at) is { } m)
            {
                packet.Constructs.UnionWith(this.SidesOf(m));
            }
        }

        private void CountTypeRef(SemanticModel model, TypeSyntax syntax, Side side)
        {
            if (model.GetSymbolInfo(syntax).Symbol is INamedTypeSymbol t && _contractTypeRefs.TryGetValue(t.OriginalDefinition, out Dictionary<Side, int>? counts))
            {
                counts[side] = (counts.TryGetValue(side, out int n) ? n : 0) + 1;
            }
        }

        private void RecordServerHandler(SemanticModel model, AttributeSyntax attr)
        {
            if (attr.ArgumentList?.Arguments.FirstOrDefault()?.Expression is not { } expr)
            {
                return;
            }

            Optional<object?> value = model.GetConstantValue(expr);
            if (!value.HasValue || value.Value is null)
            {
                return;
            }

            ushort op = Convert.ToUInt16(value.Value, CultureInfo.InvariantCulture);
            foreach (PacketInfo p in _packets.Values.Where(p => p.OpCode == op))
            {
                p.Handles.Add(Side.Server);
            }
        }

        private void RecordGenericUse(SemanticModel model, GenericNameSyntax generic, Side fileSide)
        {
            ISymbol? symbol = model.GetSymbolInfo(generic).Symbol ?? model.GetSymbolInfo(generic).CandidateSymbols.FirstOrDefault();
            ImmutableArray<ITypeSymbol> typeArgs = symbol switch
            {
                IMethodSymbol m => m.TypeArguments,
                INamedTypeSymbol t => t.TypeArguments,
                _ => [],
            };

            foreach (ITypeSymbol arg in typeArgs)
            {
                if (!_packets.TryGetValue(arg.OriginalDefinition, out PacketInfo? packet))
                {
                    continue;
                }

                string name = generic.Identifier.ValueText;
                if (name is "PacketFactory" || name.Contains("Send", StringComparison.Ordinal))
                {
                    this.Constructs(packet, model, generic);
                }
                else if (fileSide == Side.Client && s_receiveMethods.Contains(name, StringComparer.Ordinal))
                {
                    packet.Handles.Add(Side.Client);
                }
            }
        }

        private static (bool Read, bool Write) Classify(SimpleNameSyntax name)
        {
            SyntaxNode expr = name.Parent is MemberAccessExpressionSyntax ma && ma.Name == name ? ma
                : name.Parent is MemberBindingExpressionSyntax mb ? mb.Parent is ConditionalAccessExpressionSyntax ca ? ca : mb
                : name;

            if (expr.Parent is AssignmentExpressionSyntax assign && assign.Left == expr)
            {
                return (!assign.IsKind(SyntaxKind.SimpleAssignmentExpression), true);
            }

            if (expr.Parent is ArgumentSyntax arg && !arg.RefKindKeyword.IsKind(SyntaxKind.None))
            {
                return (arg.RefKindKeyword.IsKind(SyntaxKind.RefKeyword), true);
            }

            if (expr.Parent is PostfixUnaryExpressionSyntax or PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.PreIncrementExpression or (int)SyntaxKind.PreDecrementExpression })
            {
                return (true, true);
            }

            // pkt.List.Add(x) / pkt.Array[i] = x fill a collection field.
            if (expr.Parent is ElementAccessExpressionSyntax element && element.Expression == expr
                && element.Parent is AssignmentExpressionSyntax elementAssign && elementAssign.Left == element)
            {
                return (false, true);
            }

            if (expr.Parent is MemberAccessExpressionSyntax call && call.Expression == expr
                && s_collectionWriters.Contains(call.Name.Identifier.ValueText, StringComparer.Ordinal))
            {
                return (false, true);
            }

            return (true, false);
        }
    }
}
