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
/// Server authority guards for <c>desktop/</c>: the client must not own gameplay balance data.
/// Gameplay numbers (damage, speeds, cooldowns, prices, recipes, XP curves, ...) must come from the server.
/// These are heuristics: they block NEW violations; existing ones are listed below and tracked in
/// docs/audit/2026-11-codebase-audit.md.
/// </summary>
public sealed partial class ServerAuthorityTests
{
    /// <summary>
    /// Engine types that hold server-side gameplay balance (stats, recipes, cooldowns, XP curves, drop/harvest tables).
    /// Pure identifiers are fine on the client, but these types also carry the balance tables.
    /// </summary>
    private static readonly string[] s_gameplayBalanceTypes =
    [
        "StarterGameCatalog",
        "SkillCatalog",
        "WorldActorCatalog",
        "PropHarvestCatalog",
        "CharacterProgressionPolicy",
        "ItemEnhancementPolicy",
        "WorldProgression",
        "ToolUsagePolicy",
        "RecipeMatcher",
        "ForgeEnhancer",
    ];

    /// <summary>
    /// Word parts that mark a numeric constant as gameplay balance (matched against PascalCase/underscore parts).
    /// </summary>
    private static readonly HashSet<string> s_gameplayTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "damage", "dmg", "health", "hp", "mp", "mana", "stamina", "cooldown", "price", "cost", "drop", "loot",
        "xp", "exp", "experience", "recipe", "attack", "defense", "armor", "hunger", "thirst", "gold", "reward",
        "respawn", "spawn", "durability", "crit", "critical", "regen", "yield", "aggro", "speed", "reach",
        "stat", "stats", "heal", "cast", "harvest",
    };

    /// <summary>
    /// Existing desktop files that use gameplay balance types. Entry format: <c>repo/relative/path.cs|TypeName</c>.
    /// Do NOT add new entries: send the data from the server (config sync) instead.
    /// </summary>
    private static readonly HashSet<string> s_balanceTypeUsageAllowlist = new(StringComparer.Ordinal)
    {
        // Debt: the client reads engine balance tables directly (finding SA-1 in the audit report).
        // Remove entries as config sync replaces each usage.
        "desktop/Carbon.Desktop.Networking/Session/MultiplayerSession.Actions.cs|ItemEnhancementPolicy",
        "desktop/Carbon.Desktop.Networking/Session/MultiplayerSession.Actions.cs|SkillCatalog",
        "desktop/Carbon.Desktop.Networking/Session/MultiplayerSession.Auth.cs|CharacterProgressionPolicy",
        "desktop/Carbon.Desktop.Networking/Session/MultiplayerSession.Auth.cs|ItemEnhancementPolicy",
        "desktop/Carbon.Desktop.Networking/Session/MultiplayerSession.Packets.cs|CharacterProgressionPolicy",
        "desktop/Carbon.Desktop.Networking/Session/MultiplayerSession.Packets.cs|ItemEnhancementPolicy",
        "desktop/Carbon.Desktop.Networking/Session/MultiplayerSession.Packets.cs|SkillCatalog",
        "desktop/Carbon.Desktop.Networking/Session/MultiplayerSession.Packets.cs|ToolUsagePolicy",
        "desktop/Carbon.Desktop.Networking/Session/MultiplayerSession.Packets.cs|WorldActorCatalog",
        "desktop/Carbon.Desktop.Networking/Session/MultiplayerSession.cs|WorldActorCatalog",
        "desktop/Carbon.Desktop.Networking/Session/MultiplayerSession.cs|WorldProgression",
        "desktop/Carbon.Desktop.Rendering/Effects/ActionVfxRenderer.cs|StarterGameCatalog",
        "desktop/Carbon.Desktop.Rendering/Hud/CharacterSheetModalWidget.cs|ItemEnhancementPolicy",
        "desktop/Carbon.Desktop.Rendering/Hud/ForgeAnvilModalWidget.cs|ItemEnhancementPolicy",
        "desktop/Carbon.Desktop.Rendering/Hud/HotbarAndItemWidget.cs|ItemEnhancementPolicy",
        "desktop/Carbon.Desktop.Rendering/Hud/HotbarAndItemWidget.cs|ToolUsagePolicy",
        "desktop/Carbon.Desktop.Rendering/Hud/InventoryModalWidget.cs|ItemEnhancementPolicy",
        "desktop/Carbon.Desktop.Rendering/Hud/InventoryDialogRenderer.cs|ItemEnhancementPolicy", // moved with the dialog text out of InventoryModalWidget
        "desktop/Carbon.Desktop.Rendering/Hud/PlayerModalTabs.cs|SkillCatalog",
        "desktop/Carbon.Desktop.Rendering/Hud/SkillBarWidget.cs|SkillCatalog",
        "desktop/Carbon.Desktop.Rendering/Overlays/WorldEntityOverlayRenderer.cs|WorldActorCatalog",
        "desktop/Carbon.Desktop.Rendering/VectorArt/ModularCharacterOverlays.cs|StarterGameCatalog",
        "desktop/Carbon.Desktop.Rendering/World/WorldSceneRenderer.cs|ItemEnhancementPolicy",
        "desktop/Carbon.Desktop.Rendering/World/WorldSceneRenderer.cs|WorldProgression",
        "desktop/Carbon.Desktop/Capture/ArtShowcaseRunner.cs|WorldActorCatalog",
        "desktop/Carbon.Desktop/Input/DesktopInputController.cs|SkillCatalog",
        "desktop/Carbon.Desktop/Scenes/CharacterSelectScene.cs|StarterGameCatalog",
        "desktop/Carbon.Desktop/Scenes/WorldGameplayScene.cs|WorldActorCatalog",

        // NEW after the guard was written: added in #30 (creatures, owned by a parallel agent). Uses archetype id
        // constants only, to pick an attack clip. Audit SA-1c: declare the clip in the monster .cvg.json instead.
        "desktop/Carbon.Desktop.Rendering/VectorArt/MonsterAttackAnimator.cs|WorldActorCatalog",

        // MOVED, not new (ADR-020): the inventory tooltip's +N stat multiplier and effective max durability were computed
        // in InventoryModalWidget (allowlisted above) and now live in the cached tooltip builder. Same calls. Audit
        // SA-1: sync the enhancement curve from the server and drop both entries.
        "desktop/Carbon.Desktop.Rendering/Hud/ItemTooltipBuilder.cs|ItemEnhancementPolicy",
    };

    /// <summary>
    /// Existing gameplay-looking numeric constants in desktop/. Entry format: <c>repo/relative/path.cs|Identifier</c>.
    /// Do NOT add new entries for gameplay values; purely visual values (animation timing, UI layout) may be renamed
    /// to avoid gameplay terms or added here with a comment.
    /// </summary>
    private static readonly HashSet<string> s_numericConstantAllowlist = new(StringComparer.Ordinal)
    {
        // Client-side movement speeds duplicated from the server for prediction. Audit: sync via config packet.
        "desktop/Carbon.Desktop.Networking/Session/MultiplayerSession.cs|SprintSpeed",
        "desktop/Carbon.Desktop.Networking/Session/MultiplayerSession.cs|WalkSpeed",

        // Visual only: terrain apron mesh extent ("reach" is a false positive).
        "desktop/Carbon.Desktop.Rendering/World/ChunkGeometryBaker.cs|apronReach",

        // Hunger state code mirrored from the survival enum (#14), used for HUD labels. Not a balance number,
        // but a duplicated wire value. Audit: use the shared HungerState enum from contracts.
        "desktop/Carbon.Desktop.Rendering/Hud/HotbarAndItemWidget.cs|UrgentHungerState",
    };

    [Fact]
    public void Desktop_ShouldNotUseNewGameplayBalanceTypes()
    {
        List<string> violations = [.. FindBalanceTypeUsages().Where(k => !s_balanceTypeUsageAllowlist.Contains(k))];
        Violations.ShouldBeEmpty(violations, "the desktop client must receive gameplay config from the server, not read engine balance tables");
    }

    [Fact]
    public void BalanceTypeUsageAllowlist_ShouldNotContainStaleEntries()
    {
        HashSet<string> found = [.. FindBalanceTypeUsages()];
        Violations.ShouldBeEmpty(s_balanceTypeUsageAllowlist.Where(e => !found.Contains(e)), "fixed entries must be removed from the allowlist");
    }

    [Fact]
    public void Desktop_ShouldNotDeclareNewGameplayNumericConstants()
    {
        List<string> violations = [.. FindGameplayNumericConstants().Where(k => !s_numericConstantAllowlist.Contains(k))];
        Violations.ShouldBeEmpty(violations, "gameplay numbers (speeds, damage, cooldowns, prices, ...) are server-owned and must be synced, not hardcoded");
    }

    [Fact]
    public void NumericConstantAllowlist_ShouldNotContainStaleEntries()
    {
        HashSet<string> found = [.. FindGameplayNumericConstants()];
        Violations.ShouldBeEmpty(s_numericConstantAllowlist.Where(e => !found.Contains(e)), "fixed entries must be removed from the allowlist");
    }

    private static IEnumerable<(string Path, string Text)> DesktopSources() =>
        RepositoryLayout.Projects
            .Where(p => p.Layer == "desktop")
            .SelectMany(RepositoryLayout.SourceFiles)
            .Select(f => (RepositoryLayout.Relative(f), StripComments(File.ReadAllText(f))));

    private static IEnumerable<string> FindBalanceTypeUsages()
    {
        foreach ((string path, string text) in DesktopSources())
        {
            foreach (string type in s_gameplayBalanceTypes)
            {
                if (Regex.IsMatch(text, $@"\b{type}\b"))
                {
                    yield return $"{path}|{type}";
                }
            }
        }
    }

    private static IEnumerable<string> FindGameplayNumericConstants()
    {
        foreach ((string path, string text) in DesktopSources())
        {
            foreach (Match match in NumericConstantRegex().Matches(text))
            {
                string name = match.Groups["name"].Value;
                if (WordParts(name).Any(s_gameplayTerms.Contains))
                {
                    yield return $"{path}|{name}";
                }
            }
        }
    }

    private static IEnumerable<string> WordParts(string identifier) =>
        WordPartRegex().Matches(identifier.TrimStart('_').Replace("s_", string.Empty, StringComparison.Ordinal)).Select(m => m.Value);

    private static string StripComments(string text) => CommentRegex().Replace(text, string.Empty);

    /// <summary>
    /// Matches <c>const</c>/<c>static readonly</c> fields of numeric scalar, numeric array, or dictionary-with-numeric-values type.
    /// </summary>
    [GeneratedRegex(@"\b(?:const|static\s+readonly)\s+(?:(?:s?byte|u?short|u?int|u?long|float|double|decimal)(?:\[\])?|(?:Frozen|IReadOnly)?Dictionary<[^;=]*,\s*(?:s?byte|u?short|u?int|u?long|float|double|decimal)>)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=")]
    private static partial Regex NumericConstantRegex();

    [GeneratedRegex(@"[A-Z]{2,}(?=[A-Z][a-z]|\d|\b|_)|[A-Z]?[a-z]+|[A-Z]+|\d+")]
    private static partial Regex WordPartRegex();

    [GeneratedRegex(@"//[^\n]*|/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex CommentRegex();
}
