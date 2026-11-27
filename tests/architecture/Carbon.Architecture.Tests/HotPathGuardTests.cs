// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Carbon.Architecture.Tests;

/// <summary>
/// Audit round 2 (docs/audit/2026-11-round2.md) regression guards for hot paths and client god classes:
/// no LINQ in code that runs every tick or every frame, and size caps on the largest client classes and methods.
/// Each allowlist only shrinks: the <c>*_ShouldNotContainStaleEntries</c> tests fail once an entry is no longer needed.
/// </summary>
public sealed class HotPathGuardTests
{
    /// <summary>
    /// Folders whose code runs on the server tick, the packet path or the client frame (AGENTS.md rule 4,
    /// <c>performance-aot</c>: "No LINQ on the Tick Path"). Cold code (startup, config, persistence, auth) is not listed.
    /// </summary>
    private static readonly string[] s_hotPathFolders =
    [
        "engine",
        "graphics",
        "server/Carbon.Application/World",
        "server/Carbon.Handlers",
        "server/Carbon.Host/WorldOutboundSink.cs",
        "desktop/Carbon.Desktop.Networking",
        "desktop/Carbon.Desktop.Rendering",
        "desktop/Carbon.Desktop/Scenes",
    ];

    /// <summary>Files in hot-path folders that may import LINQ because the code is a cold load path. Do NOT add entries.</summary>
    private static readonly HashSet<string> s_linqAllowlist = new(StringComparer.Ordinal)
    {
        // Loads and validates the .cvg.json art library once per scene, never per frame.
        "desktop/Carbon.Desktop.Rendering/VectorArt/CarbonVectorArtLibrary.cs",
    };

    /// <summary>Folders whose classes and methods are size-capped (the client; the server world has its own cap in <see cref="ClassSizeTests"/>).</summary>
    private static readonly string[] s_sizeCappedFolders = ["desktop", "graphics"];

    /// <summary>A class (all partial declarations together) longer than this must be split.</summary>
    private const int MaxClassLines = 600;

    /// <summary>A method, constructor, accessor or local function body longer than this must be split.</summary>
    private const int MaxMethodLines = 120;

    /// <summary>
    /// Client classes over <see cref="MaxClassLines"/>, with the lines they may not exceed: their size on master when the
    /// guard was added (2026-11-27), rounded up to the next 50 so client PRs already in flight still fit. Lower a cap
    /// whenever the class shrinks and delete the entry once it fits. Never raise a cap.
    /// </summary>
    private static readonly Dictionary<string, int> s_classSizeAllowlist = new(StringComparer.Ordinal)
    {
        ["Carbon.Desktop.Networking.Session.MultiplayerSession"] = 4123, // TODO(audit round 2): split by responsibility (auth, packets, actions, inventory) and lower; 4123 after #77/#81
        ["Carbon.Desktop.Rendering.Hud.CraftingModalWidget"] = 650, // 644 today
        ["Carbon.Desktop.Rendering.Hud.InventoryModalWidget"] = 876, // TODO(audit round 2): split dialogs/tooltip/paper doll out of the modal and lower; 876 after #77/#81
        ["Carbon.Desktop.Rendering.VectorArt.CarbonVectorArtLibrary"] = 950, // 920 today
        ["Carbon.Desktop.Rendering.VectorArt.ModularCharacterOverlays"] = 650, // 625 today
        ["Carbon.Desktop.Rendering.VectorArt.VectorRigRenderer"] = 750, // 741 today
        ["Carbon.Desktop.Rendering.World.ChunkGeometryBaker"] = 950, // 935 today: the bake half of ChunkTerrainRenderer (1,154 before the split)
        ["Carbon.Desktop.Rendering.World.PointOfInterestRenderer"] = 1150, // 1125 today
        ["Carbon.Desktop.Rendering.World.WorldSceneRenderer"] = 1000, // 986 today
        ["Carbon.Desktop.Scenes.CharacterSelectScene"] = 900, // 885 today
        ["Carbon.Desktop.Scenes.LoginScene"] = 700, // 665 today
        ["Carbon.Desktop.Scenes.WorldGameplayScene"] = 1113, // TODO(audit round 2): split input/modal routing out of the scene and lower; 1113 after #77/#81
        ["Carbon.Graphics.Fonts.TrueType.TrueTypeFontLoader"] = 1000, // 953 today
    };

    /// <summary>
    /// Client methods over <see cref="MaxMethodLines"/>, keyed <c>Type.Member</c>, capped at their length on master when the
    /// guard was added, rounded up to the next 10. Same rules as the class allowlist.
    /// </summary>
    private static readonly Dictionary<string, int> s_methodSizeAllowlist = new(StringComparer.Ordinal)
    {
        ["Carbon.Desktop.Capture.ArtShowcaseRunner.RenderAndCapture"] = 140, // 133 today
        ["Carbon.Desktop.Networking.Session.MultiplayerSession.EnterWorldAsync"] = 130, // 128 today
        ["Carbon.Desktop.Networking.Session.MultiplayerSession.ReconnectFullSessionBackgroundAsync"] = 140, // 132 today
        ["Carbon.Desktop.Networking.Session.MultiplayerSession.Update"] = 190, // 189 today
        ["Carbon.Desktop.Rendering.Hud.HudOverlayRenderer.Render"] = 180, // 173 today
        ["Carbon.Desktop.Rendering.Overlays.WorldEntityOverlayRenderer.DrawGeometry"] = 140, // 139 today
        ["Carbon.Desktop.Rendering.VectorArt.CarbonVectorArtLibrary.LoadFromDirectory"] = 200, // 195 today
        ["Carbon.Desktop.Rendering.VectorArt.VectorRigRenderer.DrawEquippedItemOnRig"] = 130, // 125 today
        ["Carbon.Desktop.Rendering.World.ChunkGeometryBaker.BakeTileCliffFaces"] = 140, // 131 today
        ["Carbon.Desktop.Rendering.World.PointOfInterestRenderer.DrawHouse"] = 190, // 185 today
        ["Carbon.Desktop.Rendering.World.PointOfInterestRenderer.DrawInterior"] = 150, // 145 today
        ["Carbon.Desktop.Rendering.World.WorldSceneRenderer.DrawRemoteEntity"] = 180, // 177 today
        ["Carbon.Desktop.Rendering.World.WorldSceneRenderer.Render"] = 220, // 210 today
        ["Carbon.Desktop.Rendering.World.WorldTreeArt.DrawCanopy"] = 170, // 160 today
        ["Carbon.Desktop.Scenes.CharacterSelectScene.PollAsyncTasks"] = 140, // 132 today
        ["Carbon.Desktop.Scenes.CharacterSelectScene.Render"] = 290, // 284 today
        ["Carbon.Desktop.Scenes.LoginScene.Render"] = 140, // 130 today
        ["Carbon.Desktop.Scenes.LoginScene.Update"] = 180, // 175 today
        ["Carbon.Desktop.Scenes.WorldGameplayScene.HandleInventoryTabInput"] = 148, // TODO(audit round 2): extract split/destroy/sort input handlers and lower; 148 after #77/#81
        ["Carbon.Desktop.Scenes.WorldGameplayScene.Update"] = 504, // TODO(audit round 2): extract per-modal update steps and lower; 504 after #77/#81
        ["Carbon.Graphics.Fonts.Layout.TextLayoutEngine.LayoutAndDraw"] = 190, // 186 today
        ["Carbon.Graphics.Fonts.Layout.TextLayoutEngine.Measure"] = 130, // 125 today
        ["Carbon.Graphics.Fonts.TrueType.TrueTypeFontLoader.Rasterize"] = 200, // 194 today
        ["Carbon.Graphics.Fonts.TrueType.TrueTypeFontLoader.TryBuildGlyphEdges"] = 170, // 163 today
        ["Carbon.Graphics.UI.Controls.TextBox.Draw"] = 130, // 122 today
    };

    [Fact]
    public void HotPathCode_ShouldNotImportLinq()
    {
        List<string> violations = [.. FindLinqImports().Where(path => !s_linqAllowlist.Contains(path))];

        Violations.ShouldBeEmpty(violations, "LINQ allocates enumerators and delegates; use loops or spans on the tick and frame paths (performance-aot)");
    }

    [Fact]
    public void LinqAllowlist_ShouldNotContainStaleEntries()
    {
        HashSet<string> found = [.. FindLinqImports()];
        Violations.ShouldBeEmpty(s_linqAllowlist.Where(e => !found.Contains(e)), "fixed entries must be removed from the allowlist");
    }

    [Fact]
    public void ClientClasses_ShouldNotExceedSizeCaps()
    {
        List<string> violations = [];
        foreach ((string name, int lines) in MeasureClasses())
        {
            int cap = s_classSizeAllowlist.TryGetValue(name, out int allowed) ? allowed : MaxClassLines;
            if (lines > cap)
            {
                violations.Add($"{name}: {lines} lines (cap {cap})");
            }
        }

        Violations.ShouldBeEmpty(violations, "split large client classes by responsibility (audit round 2, SRP-*); never raise a cap");
    }

    [Fact]
    public void ClassSizeAllowlist_ShouldNotContainStaleEntries()
    {
        Dictionary<string, int> sizes = MeasureClasses();
        Violations.ShouldBeEmpty(
            s_classSizeAllowlist.Keys.Where(name => !sizes.TryGetValue(name, out int lines) || lines <= MaxClassLines),
            "classes that now fit the general cap must be removed from the allowlist");
    }

    [Fact]
    public void ClientMethods_ShouldNotExceedSizeCaps()
    {
        List<string> violations = [];
        foreach ((string name, int lines) in MeasureMethods())
        {
            int cap = s_methodSizeAllowlist.TryGetValue(name, out int allowed) ? allowed : MaxMethodLines;
            if (lines > cap)
            {
                violations.Add($"{name}: {lines} lines (cap {cap})");
            }
        }

        Violations.ShouldBeEmpty(violations, "extract long client methods into named steps or collaborators (audit round 2, SRP-*); never raise a cap");
    }

    [Fact]
    public void MethodSizeAllowlist_ShouldNotContainStaleEntries()
    {
        Dictionary<string, int> sizes = MeasureMethods();
        Violations.ShouldBeEmpty(
            s_methodSizeAllowlist.Keys.Where(name => !sizes.TryGetValue(name, out int lines) || lines <= MaxMethodLines),
            "methods that now fit the general cap must be removed from the allowlist");
    }

    private static IEnumerable<string> FindLinqImports()
    {
        foreach ((string path, CompilationUnitSyntax root) in ParseSources(s_hotPathFolders))
        {
            bool importsLinq = root.DescendantNodes().OfType<UsingDirectiveSyntax>()
                .Any(u => u.Name is not null && IsLinqNamespace(u.Name.ToString()));
            bool qualifiesLinq = root.DescendantNodes().OfType<QualifiedNameSyntax>()
                .Any(q => q.Parent is not QualifiedNameSyntax && q.Parent is not UsingDirectiveSyntax && IsLinqNamespace(q.Left.ToString()));
            if (importsLinq || qualifiesLinq)
            {
                yield return path;
            }
        }
    }

    // System.Xml.Linq is XML, not LINQ-to-objects.
    private static bool IsLinqNamespace(string name) =>
        name is "System.Linq" or "global::System.Linq" || name.StartsWith("System.Linq.", StringComparison.Ordinal);

    /// <summary>Lines per class, summed across partial declarations, keyed by the namespace-qualified type name.</summary>
    private static Dictionary<string, int> MeasureClasses()
    {
        Dictionary<string, int> sizes = new(StringComparer.Ordinal);
        foreach ((string _, CompilationUnitSyntax root) in ParseSources(s_sizeCappedFolders))
        {
            foreach (TypeDeclarationSyntax type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                // Nested types count toward their own entry, not the outer one.
                int nested = type.Members.OfType<TypeDeclarationSyntax>().Sum(LineCount);
                string name = QualifiedName(type);
                sizes[name] = sizes.GetValueOrDefault(name) + LineCount(type) - nested;
            }
        }

        return sizes;
    }

    /// <summary>Body lines per member with a body, keyed <c>Type.Member</c> (overloads keep the longest).</summary>
    private static Dictionary<string, int> MeasureMethods()
    {
        Dictionary<string, int> sizes = new(StringComparer.Ordinal);
        foreach ((string _, CompilationUnitSyntax root) in ParseSources(s_sizeCappedFolders))
        {
            foreach (SyntaxNode node in root.DescendantNodes())
            {
                (string? member, SyntaxNode? body) = node switch
                {
                    MethodDeclarationSyntax m => (m.Identifier.Text, (SyntaxNode?)m.Body ?? m.ExpressionBody),
                    ConstructorDeclarationSyntax c => (".ctor", (SyntaxNode?)c.Body ?? c.ExpressionBody),
                    LocalFunctionStatementSyntax l => (l.Identifier.Text, (SyntaxNode?)l.Body ?? l.ExpressionBody),
                    AccessorDeclarationSyntax a => (a.Keyword.Text, (SyntaxNode?)a.Body ?? a.ExpressionBody),
                    _ => (null, null),
                };
                TypeDeclarationSyntax? owner = node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
                if (member is null || body is null || owner is null)
                {
                    continue;
                }

                string key = $"{QualifiedName(owner)}.{member}";
                sizes[key] = Math.Max(sizes.GetValueOrDefault(key), LineCount(body));
            }
        }

        return sizes;
    }

    private static int LineCount(SyntaxNode node)
    {
        FileLinePositionSpan span = node.GetLocation().GetLineSpan();
        return span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
    }

    private static string QualifiedName(TypeDeclarationSyntax type)
    {
        string name = type.Identifier.Text;
        for (SyntaxNode? parent = type.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is TypeDeclarationSyntax outer)
            {
                name = outer.Identifier.Text + "." + name;
            }
            else if (parent is BaseNamespaceDeclarationSyntax ns)
            {
                name = ns.Name + "." + name;
            }
        }

        return name;
    }

    private static IEnumerable<(string Path, CompilationUnitSyntax Root)> ParseSources(string[] folders)
    {
        foreach (string folder in folders)
        {
            foreach (string relative in RepositoryLayout.TrackedFiles(folder))
            {
                if (!relative.EndsWith(".cs", StringComparison.Ordinal))
                {
                    continue;
                }

                string text = File.ReadAllText(Path.Combine(RepositoryLayout.Root, relative));
                yield return (relative, CSharpSyntaxTree.ParseText(text, path: relative).GetCompilationUnitRoot());
            }
        }
    }
}
