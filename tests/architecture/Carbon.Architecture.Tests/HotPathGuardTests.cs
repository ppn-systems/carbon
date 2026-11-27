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
/// Audit round 2 (docs/audit/2026-11-round2.md) regression guard for hot paths: no LINQ in code that runs every tick
/// or every frame. The allowlist only shrinks: <see cref="LinqAllowlist_ShouldNotContainStaleEntries"/> fails once an
/// entry is no longer needed. (Line-count caps were removed: they measured no risk and had to be edited on every PR.)
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
        "desktop/Carbon.Desktop/Gameplay",
        "desktop/Carbon.Desktop/Menu",
        "desktop/Carbon.Desktop/Capture",
    ];

    /// <summary>Files in hot-path folders that may import LINQ because the code is a cold load path. Do NOT add entries.</summary>
    private static readonly HashSet<string> s_linqAllowlist = new(StringComparer.Ordinal)
    {
        // Loads and validates the .cvg.json art library once per scene, never per frame.
        "desktop/Carbon.Desktop.Rendering/VectorArt/CarbonVectorArtLibrary.cs",
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
