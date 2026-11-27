// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Carbon.Architecture.Tests;

/// <summary>
/// Audit DOM-1: damage mitigation exists once. Creature armor lives in <c>ArmorDamageMitigation</c>, player defense and
/// magic resist in <c>ProgressionDamageMitigation</c> (over <c>progression.json</c>), and every damage path resolves a
/// hit through <c>CombatDamage.TryResolve</c>, which also enforces invulnerability.
/// </summary>
public sealed partial class DamageMitigationTests
{
    private const string ArmorPolicyPath = "engine/Carbon.Combat/Damage/ArmorDamageMitigation.cs";
    private const string PlayerPolicyPath = "server/Carbon.Application/World/Progression/ProgressionDamageMitigation.cs";

    private static readonly string[] s_scannedLayers = ["engine", "server"];

    [Fact]
    public void ArmorFormula_OnlyInTheArmorPolicy()
    {
        string[] copies = [.. SourceFiles()
            .Where(f => !string.Equals(f.Path, ArmorPolicyPath, StringComparison.Ordinal) && ArmorFormula().IsMatch(f.Text))
            .Select(f => f.Path)];

        copies.Should().BeEmpty("the 100 / (100 + armor) rule must only live in ArmorDamageMitigation (audit DOM-1)");
        File.ReadAllText(Path.Combine(RepositoryLayout.Root, ArmorPolicyPath)).Should().MatchRegex(ArmorFormula().ToString());
    }

    [Fact]
    public void PlayerMitigation_OnlyThroughThePlayerPolicy()
    {
        string[] callers = [.. SourceFiles()
            .Where(f => f.Path.StartsWith("server/", StringComparison.Ordinal) &&
                !string.Equals(f.Path, PlayerPolicyPath, StringComparison.Ordinal) &&
                ConfigMitigateCall().IsMatch(f.Text))
            .Select(f => f.Path)];

        callers.Should().BeEmpty("server code must mitigate player hits through ProgressionDamageMitigation and CombatDamage.TryResolve");
    }

    [Fact]
    public void ServerDamagePaths_ResolveThroughCombatDamage()
    {
        string[] paths =
        [
            "server/Carbon.Application/World/Combat/CombatService.cs",
            "server/Carbon.Application/World/Progression/CharacterProgressionService.cs",
        ];

        foreach (string path in paths)
        {
            File.ReadAllText(Path.Combine(RepositoryLayout.Root, path))
                .Should().Contain("CombatDamage.TryResolve(", $"{path} resolves hits through the shared damage rule (audit DOM-1)");
        }
    }

    private static IEnumerable<(string Path, string Text)> SourceFiles() =>
        s_scannedLayers
            .SelectMany(RepositoryLayout.TrackedFiles)
            .Where(p => p.EndsWith(".cs", StringComparison.Ordinal) && File.Exists(Path.Combine(RepositoryLayout.Root, p)))
            .Select(p => (p, CommentRegex().Replace(File.ReadAllText(Path.Combine(RepositoryLayout.Root, p)), string.Empty)));

    [GeneratedRegex(@"100f\s*/\s*\(\s*100f\s*\+")]
    private static partial Regex ArmorFormula();

    [GeneratedRegex(@"\.Mitigate\(\s*\w+\s*,\s*(?:DamageKind\.|kind\b)")]
    private static partial Regex ConfigMitigateCall();

    [GeneratedRegex(@"//[^\n]*|/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex CommentRegex();
}
