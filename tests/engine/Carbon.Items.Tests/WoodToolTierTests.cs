// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Crafting;
using Carbon.Items.Definitions;
using Carbon.Items.Progression;
using FluentAssertions;
using Xunit;

namespace Carbon.Items.Tests;

/// <summary>Wood tools are the first tier after bare hands: made by hand, weaker than stone (workbench), then metal (forge).</summary>
public sealed class WoodToolTierTests
{
    private static readonly ItemRules s_rules = ItemRules.LoadShippedDefaults();
    private static readonly ProgressionConfig s_progression = ProgressionConfig.LoadShippedDefaults();

    [Theory]
    [InlineData("wood_axe", CraftingStation.None)]
    [InlineData("wood_pickaxe", CraftingStation.None)]
    [InlineData("wood_sword", CraftingStation.None)]
    [InlineData("stone_axe", CraftingStation.Workbench)]
    [InlineData("stone_pickaxe", CraftingStation.Workbench)]
    [InlineData("copper_sword", CraftingStation.Forge)]
    [InlineData("iron_axe", CraftingStation.Forge)]
    public void Recipes_ToolTiers_AreMadeByHandThenAtTheWorkbenchThenAtTheForge(string code, CraftingStation station)
    {
        s_rules.RecipeCatalog.TryGetByCode(code, out CraftingRecipe? recipe).Should().BeTrue();

        recipe!.Station.Should().Be(station);
    }

    [Theory]
    [InlineData("wood_axe", ItemMaterial.Wood)]
    [InlineData("wood_pickaxe", ItemMaterial.Wood)]
    [InlineData("wood_sword", ItemMaterial.Wood)]
    [InlineData("stone_axe", ItemMaterial.Stone)]
    [InlineData("stone_pickaxe", ItemMaterial.Stone)]
    public void Items_ToolTiers_StateTheirMaterial(string code, ItemMaterial material) =>
        Archetype(code).Material.Should().Be(material, "the repair rules price a tool by what it is made of");

    [Theory]
    [InlineData("wood_axe", ToolTargetKind.Flora)]
    [InlineData("wood_pickaxe", ToolTargetKind.Mineral)]
    public void EvaluatePropStrike_WoodTool_BreaksTierOneNodesThatBareHandsCannot(string code, ToolTargetKind target)
    {
        ItemArchetype tool = Archetype(code);
        ItemSlot slot = new(tool.Id, 1, 0, tool.MaxDurability);

        PropStrikeEvaluation strike = ToolUsagePolicy.EvaluatePropStrike(s_rules, in slot, target, 1);
        PropStrikeEvaluation hands = ToolUsagePolicy.EvaluatePropStrike(s_rules, ItemSlot.Empty, target, 1);

        strike.Outcome.Should().Be(ToolStrikeOutcome.Optimal);
        strike.EffectiveTier.Should().Be(1);
        strike.Damage.Should().BeGreaterThan(hands.Damage, $"{code} beats bare hands");
    }

    [Fact]
    public void EvaluatePropStrike_TierTwoTree_FallsToTheStoneAxeButNotTheWoodAxe()
    {
        ItemSlot wood = Full("wood_axe");
        ItemSlot stone = Full("stone_axe");

        ToolUsagePolicy.EvaluatePropStrike(s_rules, in wood, ToolTargetKind.Flora, 2).Outcome.Should().Be(ToolStrikeOutcome.InsufficientTier);
        ToolUsagePolicy.EvaluatePropStrike(s_rules, in stone, ToolTargetKind.Flora, 2).Outcome.Should().Be(ToolStrikeOutcome.Optimal);
    }

    [Theory]
    [InlineData("wood_axe", "stone_axe", ToolTargetKind.Flora)]
    [InlineData("wood_pickaxe", "stone_pickaxe", ToolTargetKind.Mineral)]
    public void StoneTool_OutperformsItsWoodCounterpart(string woodCode, string stoneCode, ToolTargetKind target)
    {
        ItemArchetype wood = Archetype(woodCode);
        ItemArchetype stone = Archetype(stoneCode);
        ItemSlot woodSlot = Full(woodCode);
        ItemSlot stoneSlot = Full(stoneCode);

        float woodHit = ToolUsagePolicy.EvaluatePropStrike(s_rules, in woodSlot, target, 1).Damage;
        float stoneHit = ToolUsagePolicy.EvaluatePropStrike(s_rules, in stoneSlot, target, 1).Damage;

        stoneHit.Should().BeGreaterThan(woodHit, $"{stoneCode} harvests harder than {woodCode}");
        stone.MaxDurability.Should().BeGreaterThan(wood.MaxDurability, $"{stoneCode} lasts longer than {woodCode}");
        stone.HarvestTier.Should().BeGreaterThan(wood.HarvestTier);
    }

    [Fact]
    public void Compute_HeldWoodSword_SwingsFasterButHitsSofterThanTheCopperSword()
    {
        ItemArchetype wood = Archetype("wood_sword");
        ItemArchetype copper = Archetype("copper_sword");

        DerivedCharacterStats withWood = StatsHolding(wood);
        DerivedCharacterStats withCopper = StatsHolding(copper);
        DerivedCharacterStats bare = CharacterStatCalculator.Compute(s_rules, s_progression, 1, new InventoryContainer(PlayerInventoryLayout.TotalCapacity), 0, null);

        withWood.AttackSpeedMultiplier.Should().BeGreaterThan(withCopper.AttackSpeedMultiplier, "the wood sword is a fast, light blade");
        withCopper.AttackSpeedMultiplier.Should().Be(bare.AttackSpeedMultiplier, "only the wood sword adds swing speed");
        wood.BaseDamage.Should().BeLessThan(copper.BaseDamage, "the forge's copper sword is the stronger weapon");
        wood.MaxDurability.Should().BeLessThan(copper.MaxDurability);
    }

    private static ItemArchetype Archetype(string code) => s_rules.Items.GetRequired(s_rules.ItemCatalog.ResolveId(code));

    private static ItemSlot Full(string code)
    {
        ItemArchetype def = Archetype(code);
        return new ItemSlot(def.Id, 1, 0, def.MaxDurability);
    }

    private static DerivedCharacterStats StatsHolding(ItemArchetype weapon)
    {
        InventoryContainer inventory = new(PlayerInventoryLayout.TotalCapacity);
        inventory.SetSlot(0, new ItemSlot(weapon.Id, 1, 0, weapon.MaxDurability));
        return CharacterStatCalculator.Compute(s_rules, s_progression, 1, inventory, 0, null);
    }
}
