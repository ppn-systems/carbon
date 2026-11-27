// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Maps.Props;
using FluentAssertions;
using Xunit;

namespace Carbon.Items.Tests;

/// <summary>A character who starts with nothing must be able to gather the first tool's materials by hand.</summary>
public sealed class BareHandHarvestTests
{
    private static readonly ItemRules s_rules = ItemRules.LoadShippedDefaults();

    [Fact]
    public void EvaluatePropStrike_BareHandOnTierOneTree_PunchesSlowerThanTheWoodAxe()
    {
        ItemSlot axe = new(StarterGameCatalog.WoodAxe, 1, 0, 120f);

        PropStrikeEvaluation punch = ToolUsagePolicy.EvaluatePropStrike(s_rules, ItemSlot.Empty, ToolTargetKind.Flora, 1);
        PropStrikeEvaluation chop = ToolUsagePolicy.EvaluatePropStrike(s_rules, in axe, ToolTargetKind.Flora, 1);

        punch.Outcome.Should().Be(ToolStrikeOutcome.Optimal, "an oak can be punched down by hand");
        punch.EffectiveTier.Should().Be(1, "the world only lets the strike through when the effective tier reaches the tree's tier");
        punch.Damage.Should().BePositive();
        punch.Damage.Should().BeLessThan(chop.Damage / 4f, "punching is slow so the first axe is worth crafting");
        punch.DurabilityLoss.Should().Be(0f, "hands do not wear");
    }

    [Fact]
    public void EvaluatePropStrike_BareHandOnTierTwoTree_IsBlocked()
    {
        PropStrikeEvaluation punch = ToolUsagePolicy.EvaluatePropStrike(s_rules, ItemSlot.Empty, ToolTargetKind.Flora, 2);

        punch.Outcome.Should().Be(ToolStrikeOutcome.InsufficientTier, "pines and cherry trees still need an axe");
        punch.EffectiveTier.Should().Be(0);
    }

    [Fact]
    public void EvaluatePropStrike_BareHandOnBoulder_IsBlocked()
    {
        PropStrikeEvaluation punch = ToolUsagePolicy.EvaluatePropStrike(s_rules, ItemSlot.Empty, ToolTargetKind.Mineral, 1);

        punch.Outcome.Should().Be(ToolStrikeOutcome.InsufficientTier, "stone by hand comes from loose stones, not boulders");
    }

    [Theory]
    [InlineData(MapPropType.LooseStones)]
    [InlineData(MapPropType.FallenBranch)]
    public void LooseForage_OneBareHandStrike_BreaksItAndDropsItsMaterial(MapPropType type)
    {
        MapProp forage = MapProp.Create(type, 0, 0, 16f, 16f, radius: 10f, isSolid: false, 0U, 0U);
        ToolTargetKind kind = forage.Category == PropCategory.Mineral ? ToolTargetKind.Mineral : ToolTargetKind.Flora;

        PropStrikeEvaluation punch = ToolUsagePolicy.EvaluatePropStrike(s_rules, ItemSlot.Empty, kind, forage.Destructibility.HarvestTier);
        PropDestructibility after = forage.Destructibility.ApplyDamage(punch.Damage);

        forage.Destructibility.HarvestTier.Should().Be(0, "loose forage is picked up by hand");
        after.IsDestroyed.Should().BeTrue("one grab picks it up");
        PropHarvestCatalog.ResolveDrop(s_rules, type, out _).IsEmpty.Should().BeFalse();
    }
}
