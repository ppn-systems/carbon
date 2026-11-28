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

    /// <summary>
    /// No <c>Enum.HasFlag</c> on the tick, packet and frame paths (audit round 3, ALLOC-13 and ALLOC-14). Do not relax
    /// this into an allowlist or a probe:
    /// <list type="bullet">
    /// <item><description><c>HasFlag</c> is boxing-free only in optimized code. A method starts unoptimized (tier 0), and
    /// there every call boxes both operands: 48 B. A short probe measured 96 B per pickaxe strike on flora and 48 B per
    /// skill access check.</description></item>
    /// <item><description>The runtime optimizes a method only after it has been called for a while, and it keeps deferring
    /// that while other new code is being compiled. A busy process, a server warming up or a full test suite, runs tier-0
    /// code far longer than a quiet one. The kill guard (<c>KillRewardAllocationTests</c>) passed alone and saw a steady
    /// 4,320 B in the full Host suite, from the melee reach check; holding tier-up off
    /// (<c>DOTNET_TC_CallCountingDelayMs=600000</c>) reproduced it exactly.</description></item>
    /// <item><description>So an allocation probe cannot guard it: a method another test already tiered up allocates nothing.
    /// The call itself is the rule. Test the bits instead: <c>(flags &amp; Flag) != 0</c> for a single-bit flag, or
    /// <c>(flags &amp; Flags) == Flags</c> for several.</description></item>
    /// </list>
    /// <c>Enum.IsDefined</c>, <c>GetValues</c> and <c>GetNames</c> have a similar hidden cost (a full GC can free the enum
    /// metadata they read, ALLOC-13), but every call in these folders is load-time validation today, so they are not banned.
    /// </summary>
    [Fact]
    public void HotPathCode_ShouldNotCallEnumHasFlag()
    {
        Violations.ShouldBeEmpty(
            FindHasFlagCalls().Select(call => $"{call.Path}:{call.Line}"),
            "Enum.HasFlag allocates until its caller is optimized; test the bits directly on the tick and frame paths (performance-aot)");
    }

    // Every x.HasFlag(y) call in hot-path code.
    private static IEnumerable<(string Path, int Line)> FindHasFlagCalls()
    {
        foreach ((string path, CompilationUnitSyntax root) in ParseSources(s_hotPathFolders))
        {
            foreach (InvocationExpressionSyntax call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (call.Expression is MemberAccessExpressionSyntax { Name.Identifier.Text: "HasFlag" })
                {
                    yield return (path, call.GetLocation().GetLineSpan().StartLinePosition.Line + 1);
                }
            }
        }
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
