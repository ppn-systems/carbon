// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Crafting;
using Carbon.Items.Serialization;
using FluentAssertions;
using Xunit;

namespace Carbon.Items.Tests;

public sealed class InventoryCodecAndCatalogTests
{
    [Fact]
    public void InventoryBinaryCodec_ShouldRoundTripStarterLoadoutAndRejectCorruptPayloads()
    {
        InventoryContainer source = new(27);
        StarterGameCatalog.GrantStarterLoadout(source);
        source.SetSlot(12, new ItemSlot(StarterGameCatalog.ManaCrystal, 17));

        byte[] encoded = InventoryBinaryCodec.Serialize(source);
        encoded.Length.Should().BeGreaterThan(5);

        InventoryContainer restored = new(27);
        InventoryBinaryCodec.TryDeserializeInto(encoded, restored).Should().BeTrue();
        restored.OccupiedSlotCount.Should().Be(source.OccupiedSlotCount);
        restored.GetSlot(0).ItemId.Should().Be(StarterGameCatalog.WoodAxe);
        restored.GetSlot(2).ItemId.Should().Be(StarterGameCatalog.IronSword);
        restored.GetSlot(12).ItemId.Should().Be(StarterGameCatalog.ManaCrystal);
        restored.GetSlot(12).Quantity.Should().Be(17);

        InventoryBinaryCodec.TryDeserializeInto([], restored).Should().BeFalse();
        InventoryBinaryCodec.TryDeserializeInto([99, 0, 0, 0, 0], restored).Should().BeFalse();
    }

    [Fact]
    public void StarterGameCatalog_AllCraftingRecipesShouldCraftEndToEnd()
    {
        InventoryContainer bag = new(27);
        _ = bag.TryAdd(new ItemSlot(StarterGameCatalog.WoodLog, 40), 99);
        _ = bag.TryAdd(new ItemSlot(StarterGameCatalog.StoneChunk, 20), 99);
        _ = bag.TryAdd(new ItemSlot(StarterGameCatalog.IronOre, 20), 99);
        _ = bag.TryAdd(new ItemSlot(StarterGameCatalog.ManaCrystal, 10), 99);

        ReadOnlySpan<CraftingRecipe> recipes = StarterGameCatalog.Recipes.All;
        recipes.Length.Should().Be(6);

        for (int i = 0; i < recipes.Length; i++)
        {
            CraftingRecipe recipe = recipes[i];
            int maxStack = StarterGameCatalog.GetMaxStackSize(recipe.Output.ItemId);
            RecipeMatcher.CanCraft(bag, recipe).Should().BeTrue();
            RecipeMatcher.TryCraft(bag, recipe, maxStack).Should().BeTrue();
        }

        bag.CountItem(StarterGameCatalog.CrystalPickaxe).Should().Be(1);
        bag.CountItem(StarterGameCatalog.IronSword).Should().Be(1);
        bag.CountItem(StarterGameCatalog.StoneWallKit).Should().Be(2);
    }

    [Fact]
    public void ToolUsagePolicy_ShouldPenalizeMisuseDeflectRockAndDegradeWhenWornOut()
    {
        ItemSlot freshSword = new(StarterGameCatalog.IronSword, 1, 0, 200f);
        ItemSlot freshAxe = new(StarterGameCatalog.WoodAxe, 1, 0, 120f);
        ItemSlot freshPick = new(StarterGameCatalog.StonePickaxe, 1, 0, 160f);
        ItemSlot wornSword = new(StarterGameCatalog.IronSword, 1, 0, 0f);

        ToolUsagePolicy.IsWornOut(in freshSword).Should().BeFalse();
        ToolUsagePolicy.IsWornOut(in wornSword).Should().BeTrue();

        PropStrikeEvaluation swordOnTree = ToolUsagePolicy.EvaluatePropStrike(in freshSword, ToolTargetKind.Flora, 1);
        swordOnTree.Outcome.Should().Be(ToolStrikeOutcome.SwordMisuseOnWood);
        swordOnTree.DurabilityLoss.Should().BeGreaterThan(3f);
        swordOnTree.Damage.Should().BeLessThan(StarterGameCatalog.GetFloraHarvestDamage(StarterGameCatalog.WoodAxe));

        PropStrikeEvaluation swordOnRock = ToolUsagePolicy.EvaluatePropStrike(in freshSword, ToolTargetKind.Mineral, 1);
        swordOnRock.Outcome.Should().Be(ToolStrikeOutcome.BladeBluntOnStone);
        swordOnRock.Damage.Should().Be(0f);
        swordOnRock.DurabilityLoss.Should().BeGreaterThan(4f);

        PropStrikeEvaluation axeOnTree = ToolUsagePolicy.EvaluatePropStrike(in freshAxe, ToolTargetKind.Flora, 1);
        axeOnTree.Outcome.Should().Be(ToolStrikeOutcome.Optimal);

        PropStrikeEvaluation pickOnOre = ToolUsagePolicy.EvaluatePropStrike(in freshPick, ToolTargetKind.Mineral, 2);
        pickOnOre.Outcome.Should().Be(ToolStrikeOutcome.Optimal);

        CombatStrikeEvaluation wornHit = ToolUsagePolicy.EvaluateCombatStrike(in wornSword);
        wornHit.Outcome.Should().Be(ToolStrikeOutcome.WornOutTool);
        wornHit.Damage.Should().BeLessThan(StarterGameCatalog.GetWeaponBaseDamage(StarterGameCatalog.IronSword));
    }
}
