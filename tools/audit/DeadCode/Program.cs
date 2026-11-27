// Copyright (c) 2026 PPN Systems. All rights reserved.

// Dead-code finder for the round-3 audit (docs/audit/2026-11-round3.md).
//
//   dotnet run --project tools/audit/DeadCode -c Release -- <repo-root> <out.tsv>
//
// Loads every product and test project with MSBuildWorkspace, binds every expression once, and records which
// source-declared symbols are referenced, and from where (product code, tests, or source-generated code only).
// IDE0051/IDE0052/IDE0060/CA1823 already run as errors in the product build, so they only see private members;
// this tool covers the internal and public surface they cannot. It reports candidates, not verdicts: a symbol used
// by reflection, by a JSON document, or from outside the repo still shows as unreferenced.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

namespace Carbon.Tools.Audit.DeadCode;

internal static class Program
{
    private static readonly ConcurrentDictionary<IMethodSymbol, byte> s_methodGroups = new(SymbolKeyComparer.Instance);
    private static readonly string[] s_productRoots = ["contracts/", "engine/", "graphics/", "server/", "desktop/"];

    private static async Task<int> Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: DeadCode <repo-root> <out.tsv>");
            return 2;
        }

        string root = Path.GetFullPath(args[0]);
        MSBuildLocator.RegisterDefaults();
        return await RunAsync(root, args[1]).ConfigureAwait(false);
    }

    private static async Task<int> RunAsync(string root, string outPath)
    {
        Stopwatch clock = Stopwatch.StartNew();
        using MSBuildWorkspace workspace = MSBuildWorkspace.Create(new Dictionary<string, string> { ["Configuration"] = "Release" });
        workspace.RegisterWorkspaceFailedHandler(e =>
        {
            if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
            {
                Console.Error.WriteLine($"workspace: {e.Diagnostic.Message}");
            }
        });

        string[] projects = Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Contains("/artifacts/", StringComparison.Ordinal) && !p.Contains("/tools/", StringComparison.Ordinal))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();
        foreach (string path in projects)
        {
            if (workspace.CurrentSolution.Projects.Any(p => string.Equals(p.FilePath, path, StringComparison.Ordinal)))
            {
                continue;
            }

            _ = await workspace.OpenProjectAsync(path).ConfigureAwait(false);
        }

        Solution solution = workspace.CurrentSolution;
        Console.Error.WriteLine($"loaded {solution.Projects.Count()} projects in {clock.Elapsed.TotalSeconds:0}s");

        // symbol -> reference origins
        ConcurrentDictionary<ISymbol, Origins> uses = new(SymbolKeyComparer.Instance);
        ConcurrentDictionary<IParameterSymbol, byte> usedParameters = new(SymbolKeyComparer.Instance);
        List<(ISymbol Symbol, string Project)> candidates = [];
        List<IMethodSymbol> methods = [];
        HashSet<ISymbol> seen = new(SymbolKeyComparer.Instance);

        foreach (Project project in solution.Projects)
        {
            Compilation? compilation = await project.GetCompilationAsync().ConfigureAwait(false);
            if (compilation is null)
            {
                continue;
            }

            foreach (Diagnostic error in compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Take(3))
            {
                Console.Error.WriteLine($"{project.Name}: {error}");
            }

            string projectPath = Relative(root, project.FilePath ?? string.Empty);
            bool isTest = projectPath.StartsWith("tests/", StringComparison.Ordinal);
            bool isProduct = s_productRoots.Any(r => projectPath.StartsWith(r, StringComparison.Ordinal));

            Parallel.ForEach(compilation.SyntaxTrees, tree =>
            {
                bool generated = IsGenerated(root, tree.FilePath);
                SemanticModel model = compilation.GetSemanticModel(tree);
                Origin origin = generated ? Origin.Generated : isTest ? Origin.Test : Origin.Product;
                foreach (SyntaxNode node in tree.GetRoot().DescendantNodes())
                {
                    if (node is not (SimpleNameSyntax or ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax
                        or ConstructorInitializerSyntax or ElementAccessExpressionSyntax or ForEachStatementSyntax
                        or PrimaryConstructorBaseTypeSyntax or ImplicitElementAccessSyntax or AttributeSyntax))
                    {
                        continue;
                    }

                    SymbolInfo info = model.GetSymbolInfo(node);
                    Record(info.Symbol, node, model, origin, uses, usedParameters);
                    foreach (ISymbol candidate in info.CandidateSymbols)
                    {
                        Record(candidate, node, model, origin, uses, usedParameters);
                    }

                    if (node is ForEachStatementSyntax loop)
                    {
                        ForEachStatementInfo each = model.GetForEachStatementInfo(loop);
                        Record(each.GetEnumeratorMethod, node, model, origin, uses, usedParameters);
                        Record(each.MoveNextMethod, node, model, origin, uses, usedParameters);
                        Record(each.CurrentProperty, node, model, origin, uses, usedParameters);
                    }
                }
            });

            if (!isProduct)
            {
                continue;
            }

            foreach (INamedTypeSymbol type in AllTypes(compilation.Assembly.GlobalNamespace))
            {
                if (!type.Locations.Any(l => l.IsInSource && !IsGenerated(root, l.SourceTree?.FilePath)) || !seen.Add(type))
                {
                    continue;
                }

                candidates.Add((type, project.Name));
                foreach (ISymbol member in type.GetMembers())
                {
                    if (member.IsImplicitlyDeclared || !member.Locations.Any(l => l.IsInSource && !IsGenerated(root, l.SourceTree?.FilePath)) || !seen.Add(member))
                    {
                        continue;
                    }

                    if (member is IMethodSymbol m && m.MethodKind is MethodKind.Ordinary)
                    {
                        methods.Add(m);
                    }

                    if (IsCandidate(member))
                    {
                        candidates.Add((member, project.Name));
                    }
                }
            }
        }

        Console.Error.WriteLine($"bound all trees in {clock.Elapsed.TotalSeconds:0}s; {candidates.Count} candidates");

        StringBuilder report = new();
        report.AppendLine("status\tkind\taccess\tsymbol\tlocation\tproject");
        int dead = 0;
        int testOnly = 0;
        int generatedOnly = 0;
        foreach ((ISymbol symbol, string project) in candidates.OrderBy(c => Location(root, c.Symbol), StringComparer.Ordinal))
        {
            Origins origins = UsesOf(symbol, uses);
            string? status = origins switch
            {
                { Product: true } => null,
                { Test: true } => "test-only",
                { Generated: true } => "generated-only",
                _ => "unreferenced",
            };

            if (status is null)
            {
                continue;
            }

            _ = status switch
            {
                "unreferenced" => dead++,
                "test-only" => testOnly++,
                _ => generatedOnly++,
            };
            report.Append(status).Append('\t')
                .Append(Kind(symbol)).Append('\t')
                .Append(symbol.DeclaredAccessibility).Append('\t')
                .Append(symbol.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat)).Append('\t')
                .Append(Location(root, symbol)).Append('\t')
                .Append(project).AppendLine();
        }

        int unusedParameters = 0;
        foreach (IMethodSymbol method in methods.OrderBy(m => Location(root, m), StringComparer.Ordinal))
        {
            if (!CanHaveUnusedParameters(method))
            {
                continue;
            }

            foreach (IParameterSymbol parameter in method.Parameters)
            {
                if (parameter.IsDiscard || parameter.Name.StartsWith('_') || usedParameters.ContainsKey(parameter))
                {
                    continue;
                }

                unusedParameters++;
                report.Append("unused-parameter\tparameter\t").Append(method.DeclaredAccessibility).Append('\t')
                    .Append(method.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat)).Append(" :: ").Append(parameter.Name).Append('\t')
                    .Append(Location(root, method)).Append('\t')
                    .Append(method.ContainingAssembly.Name).AppendLine();
            }
        }

        await File.WriteAllTextAsync(outPath, report.ToString()).ConfigureAwait(false);
        Console.Error.WriteLine($"unreferenced {dead}, test-only {testOnly}, generated-only {generatedOnly}, unused parameters {unusedParameters}; {clock.Elapsed.TotalSeconds:0}s");
        return 0;
    }

    private static void Record(ISymbol? symbol, SyntaxNode node, SemanticModel model, Origin origin, ConcurrentDictionary<ISymbol, Origins> uses, ConcurrentDictionary<IParameterSymbol, byte> usedParameters)
    {
        if (symbol is null)
        {
            return;
        }

        if (symbol is IParameterSymbol parameter)
        {
            _ = usedParameters.TryAdd((IParameterSymbol)parameter.OriginalDefinition, 0);

            // A primary-constructor parameter used in the body counts as used; so does the record property it becomes.
            return;
        }

        if (symbol is IMethodSymbol { ReducedFrom: { } reduced })
        {
            symbol = reduced;
        }

        symbol = symbol.OriginalDefinition;
        if (IsInsideOwnDeclaration(symbol, node))
        {
            return;
        }

        foreach (ISymbol target in Expand(symbol))
        {
            _ = uses.AddOrUpdate(target, _ => Origins.From(origin), (_, o) => o.With(origin));
        }

        // A member access `X.Y` also uses the containing types of Y, but only from outside their own declarations:
        // a type whose members only call each other is still dead.
        for (INamedTypeSymbol? containing = symbol.ContainingType; containing is not null; containing = containing.ContainingType)
        {
            if (!IsInsideDeclaration(containing.OriginalDefinition, node))
            {
                _ = uses.AddOrUpdate(containing.OriginalDefinition, _ => Origins.From(origin), (_, o) => o.With(origin));
            }
        }

        if (symbol is IMethodSymbol { MethodKind: MethodKind.Ordinary } method && IsMethodGroup(node))
        {
            _ = s_methodGroups.TryAdd(method, 0);
        }
    }

    // `Foo` or `x.Foo` that is not the callee of an invocation is a method group (a delegate target), which must keep
    // its delegate's signature, so its parameters are not reported as unused.
    private static bool IsMethodGroup(SyntaxNode node)
    {
        if (node is not SimpleNameSyntax)
        {
            return false;
        }

        SyntaxNode expression = node.Parent is MemberAccessExpressionSyntax access && access.Name == node ? access : node;
        return expression.Parent is not InvocationExpressionSyntax invocation || invocation.Expression != expression;
    }

    private static IEnumerable<ISymbol> Expand(ISymbol symbol)
    {
        yield return symbol;
        switch (symbol)
        {
            case IMethodSymbol { PartialDefinitionPart: { } definition }:
                yield return definition;
                break;
            case IMethodSymbol { PartialImplementationPart: { } implementation }:
                yield return implementation;
                break;
            case IMethodSymbol { AssociatedSymbol: { } property }:
                yield return property;
                break;
            case IPropertySymbol { PartialDefinitionPart: { } p }:
                yield return p;
                break;
            case IPropertySymbol { PartialImplementationPart: { } p }:
                yield return p;
                break;
        }
    }

    private static bool IsInsideOwnDeclaration(ISymbol symbol, SyntaxNode node) => IsInsideDeclaration(symbol, node);

    private static bool IsInsideDeclaration(ISymbol symbol, SyntaxNode node)
    {
        foreach (SyntaxReference reference in symbol.DeclaringSyntaxReferences)
        {
            if (reference.SyntaxTree == node.SyntaxTree && reference.Span.Contains(node.Span))
            {
                return true;
            }
        }

        return false;
    }

    private static Origins UsesOf(ISymbol symbol, ConcurrentDictionary<ISymbol, Origins> uses)
    {
        Origins result = uses.TryGetValue(symbol, out Origins o) ? o : default;

        // A member that implements an interface member, or overrides one, is used when the base member is.
        if (symbol.ContainingType is { } type)
        {
            foreach (INamedTypeSymbol iface in type.AllInterfaces)
            {
                foreach (ISymbol member in iface.GetMembers())
                {
                    ISymbol? impl = type.FindImplementationForInterfaceMember(member);
                    if (impl is not null && SymbolKeyComparer.Instance.Equals(impl.OriginalDefinition, symbol) && uses.TryGetValue(member.OriginalDefinition, out Origins b))
                    {
                        result = result.With(b);
                    }
                }
            }
        }

        ISymbol? overridden = symbol switch
        {
            IMethodSymbol m => m.OverriddenMethod,
            IPropertySymbol p => p.OverriddenProperty,
            IEventSymbol e => e.OverriddenEvent,
            _ => null,
        };
        while (overridden is not null)
        {
            if (uses.TryGetValue(overridden.OriginalDefinition, out Origins b))
            {
                result = result.With(b);
            }

            overridden = overridden switch
            {
                IMethodSymbol m => m.OverriddenMethod,
                IPropertySymbol p => p.OverriddenProperty,
                IEventSymbol e => e.OverriddenEvent,
                _ => null,
            };
        }

        return result;
    }

    private static bool IsCandidate(ISymbol member)
    {
        if (member.GetAttributes().Length > 0 || member.IsOverride || member.IsExtern)
        {
            return false;
        }

        if (member.ContainingType is { TypeKind: TypeKind.Enum })
        {
            return false; // enum values are written to disk and the wire by number; listed by the enum pass instead
        }

        return member switch
        {
            IMethodSymbol m => m.MethodKind is MethodKind.Ordinary && m.ExplicitInterfaceImplementations.IsEmpty && m.Name != "Main"
                && m.Name != "Dispose" && m.Name != "GetHashCode" && m.Name != "Equals" && m.Name != "ToString" && m.Name != "PrintMembers"
                && m.Name != "Deconstruct",
            IPropertySymbol p => p.ExplicitInterfaceImplementations.IsEmpty && !p.IsIndexer && p.Name != "EqualityContract",
            IFieldSymbol f => !f.IsImplicitlyDeclared,
            IEventSymbol e => e.ExplicitInterfaceImplementations.IsEmpty,
            _ => false,
        };
    }

    private static bool CanHaveUnusedParameters(IMethodSymbol method)
    {
        if (method.IsAbstract || method.IsVirtual || method.IsOverride || method.IsExtern || method.IsPartialDefinition
            || method.ContainingType.TypeKind == TypeKind.Interface || !method.ExplicitInterfaceImplementations.IsEmpty
            || method.GetAttributes().Length > 0 || method.Parameters.Length == 0)
        {
            return false;
        }

        // An implicit interface implementation keeps the interface's signature.
        foreach (INamedTypeSymbol iface in method.ContainingType.AllInterfaces)
        {
            foreach (ISymbol member in iface.GetMembers())
            {
                if (SymbolKeyComparer.Instance.Equals(method.ContainingType.FindImplementationForInterfaceMember(member), method))
                {
                    return false;
                }
            }
        }

        // A body is needed to judge; expression-bodied and block-bodied both count.
        return method.DeclaringSyntaxReferences.Any(r => r.GetSyntax() is MethodDeclarationSyntax { Body: not null } or MethodDeclarationSyntax { ExpressionBody: not null } or LocalFunctionStatementSyntax)
            && !IsUsedAsMethodGroup(method);
    }

    private static bool IsUsedAsMethodGroup(IMethodSymbol method) => s_methodGroups.ContainsKey(method);

    private static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceSymbol ns)
    {
        foreach (INamespaceOrTypeSymbol member in ns.GetMembers())
        {
            if (member is INamespaceSymbol child)
            {
                foreach (INamedTypeSymbol t in AllTypes(child))
                {
                    yield return t;
                }
            }
            else if (member is INamedTypeSymbol type)
            {
                foreach (INamedTypeSymbol t in WithNested(type))
                {
                    yield return t;
                }
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> WithNested(INamedTypeSymbol type)
    {
        yield return type;
        foreach (INamedTypeSymbol nested in type.GetTypeMembers())
        {
            foreach (INamedTypeSymbol t in WithNested(nested))
            {
                yield return t;
            }
        }
    }

    private static bool IsGenerated(string root, string? path) =>
        string.IsNullOrEmpty(path) || !path.StartsWith(root, StringComparison.Ordinal) || path.Contains("/artifacts/", StringComparison.Ordinal)
        || path.Contains("/obj/", StringComparison.Ordinal) || path.EndsWith(".g.cs", StringComparison.Ordinal);

    private static string Kind(ISymbol symbol) => symbol switch
    {
        INamedTypeSymbol t => t.TypeKind.ToString().ToLowerInvariant(),
        IMethodSymbol => "method",
        IPropertySymbol => "property",
        IFieldSymbol f => f.IsConst ? "const" : "field",
        IEventSymbol => "event",
        _ => symbol.Kind.ToString().ToLowerInvariant(),
    };

    private static string Location(string root, ISymbol symbol)
    {
        Location? location = symbol.Locations.FirstOrDefault(l => l.IsInSource && !IsGenerated(root, l.SourceTree?.FilePath));
        if (location is null)
        {
            return string.Empty;
        }

        FileLinePositionSpan span = location.GetLineSpan();
        return $"{Relative(root, span.Path)}:{span.StartLinePosition.Line + 1}";
    }

    private static string Relative(string root, string path) =>
        path.StartsWith(root, StringComparison.Ordinal) ? path[(root.Length + 1)..] : path;

    /// <summary>
    /// Compares symbols by assembly and documentation id. Each project has its own compilation, and the same source symbol
    /// seen through a project reference is not always the same instance, so reference equality would miss
    /// cross-project uses.
    /// </summary>
    private sealed class SymbolKeyComparer : IEqualityComparer<ISymbol>
    {
        public static readonly SymbolKeyComparer Instance = new();

        public bool Equals(ISymbol? x, ISymbol? y) => x is not null && y is not null && string.Equals(Key(x), Key(y), StringComparison.Ordinal);

        public int GetHashCode(ISymbol obj) => StringComparer.Ordinal.GetHashCode(Key(obj));

        private static string Key(ISymbol symbol) => symbol switch
        {
            IParameterSymbol { ContainingSymbol: { } owner } p => Key(owner) + "#" + p.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => (symbol.ContainingAssembly?.Name ?? string.Empty) + "|" + (symbol.OriginalDefinition.GetDocumentationCommentId() ?? symbol.ToDisplayString()),
        };
    }

    private enum Origin
    {
        Product,
        Test,
        Generated,
    }

    private readonly record struct Origins(bool Product, bool Test, bool Generated)
    {
        public static Origins From(Origin origin) => default(Origins).With(origin);

        public Origins With(Origin origin) => origin switch
        {
            Origin.Product => this with { Product = true },
            Origin.Test => this with { Test = true },
            _ => this with { Generated = true },
        };

        public Origins With(Origins other) => new(this.Product || other.Product, this.Test || other.Test, this.Generated || other.Generated);
    }
}
