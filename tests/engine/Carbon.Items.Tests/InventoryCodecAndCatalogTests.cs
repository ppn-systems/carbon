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
        InventoryContainer bag = new(PlayerInventoryLayout.TotalCapacity);
        _ = bag.TryAdd(new ItemSlot(StarterGameCatalog.WoodLog, 90), 99);
        _ = bag.TryAdd(new ItemSlot(StarterGameCatalog.WoodLog, 60), 99);
        _ = bag.TryAdd(new ItemSlot(StarterGameCatalog.StoneChunk, 90), 99);
        _ = bag.TryAdd(new ItemSlot(StarterGameCatalog.IronOre, 90), 99);
        _ = bag.TryAdd(new ItemSlot(StarterGameCatalog.IronOre, 90), 99);
        _ = bag.TryAdd(new ItemSlot(StarterGameCatalog.ManaCrystal, 60), 99);
        _ = bag.TryAdd(new ItemSlot(StarterGameCatalog.BerryHerb, 20), 99);

        ReadOnlySpan<CraftingRecipe> recipes = StarterGameCatalog.Recipes.All;
        recipes.Length.Should().Be(17);

        for (int i = 0; i < recipes.Length; i++)
        {
            CraftingRecipe recipe = recipes[i];
            int maxStack = StarterGameCatalog.GetMaxStackSize(recipe.Output.ItemId);
            RecipeMatcher.CanCraft(bag, recipe).Should().BeTrue($"recipe {recipe.Id} should be craftable");
            RecipeMatcher.TryCraft(bag, recipe, maxStack).Should().BeTrue($"recipe {recipe.Id} should craft cleanly");
        }

        bag.CountItem(StarterGameCatalog.CrystalPickaxe).Should().Be(1);
        bag.CountItem(StarterGameCatalog.IronSword).Should().Be(1);
        bag.CountItem(StarterGameCatalog.StoneWallKit).Should().Be(2);
        bag.CountItem(StarterGameCatalog.IronHelmet).Should().Be(1);
        bag.CountItem(StarterGameCatalog.CrystalRobe).Should().Be(1);
    }

    [Fact]
    public void PlayerInventoryLayout_ItemEnhancement_And_CharacterProgression_ShouldEnforceRpgRules()
    {
        InventoryContainer inv = new(PlayerInventoryLayout.TotalCapacity);
        inv.SetSlot(9, new ItemSlot(StarterGameCatalog.IronHelmet, 1, 0UL, 220f));
        inv.SetSlot(10, new ItemSlot(StarterGameCatalog.WoodLog, 5));

        ItemSlot logSlot = inv.GetSlot(10);
        PlayerInventoryLayout.CanAcceptSlotItem(PlayerInventoryLayout.HeadSlot, in logSlot).Should().BeFalse();

        PlayerInventoryLayout.TryQuickMove(inv, 9, out int equippedSlot).Should().BeTrue();
        equippedSlot.Should().Be(PlayerInventoryLayout.HeadSlot);
        inv.GetSlot(PlayerInventoryLayout.HeadSlot).ItemId.Should().Be(StarterGameCatalog.IronHelmet);

        ItemSlot helmet = inv.GetSlot(PlayerInventoryLayout.HeadSlot);
        ItemSlot upgraded = ItemEnhancementPolicy.WithUpgradeLevel(in helmet, 5);
        ItemEnhancementPolicy.GetUpgradeLevel(in upgraded).Should().Be(5);
        inv.SetSlot(PlayerInventoryLayout.HeadSlot, in upgraded);

        DerivedCharacterStats stats = CharacterProgressionPolicy.ComputeStats(3, inv);
        stats.Defense.Should().BeGreaterThan(10f);
        stats.MaxHealth.Should().BeGreaterThan(140f);
        stats.MaxMana.Should().Be(100f);
        CharacterProgressionPolicy.GetXpToNextLevel(1).Should().Be(100U);
        CharacterProgressionPolicy.MitigateDamage(100f, stats.Defense).Should().BeLessThan(70f);

        ItemEnhancementPolicy.CanUpgrade(in upgraded, out uint m1, out int c1, out uint m2, out int c2, out float chance).Should().BeTrue();
        m1.Should().Be(StarterGameCatalog.IronOre);
        c1.Should().BeGreaterThan(0);
        m2.Should().Be(StarterGameCatalog.ManaCrystal);
        c2.Should().BeGreaterThan(0);
        chance.Should().BeApproximately(0.85f, 0.01f);
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
