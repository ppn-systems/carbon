// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Contracts.Common;
using Carbon.Items.Containers;
using Carbon.Items.Crafting;
using FluentAssertions;
using Xunit;

namespace Carbon.Items.Tests;

public sealed class AtomicCraftingTests
{
    private const uint Log = 1;
    private const uint Plank = 2;
    private const uint Sawdust = 3;
    private const uint Sword = 4;

    private static readonly IStackSizeProvider s_stacks = new FixedStackSizeProvider(10);

    private static CraftingRecipe SawRecipe(int workTicks = 0) => new(
        Id: 1,
        Ingredients: [new RecipeIngredient(Log, 1)],
        Output: new ItemSlot(Plank, 4),
        Byproducts: [new ItemSlot(Sawdust, 2)],
        Station: CraftingStation.Workbench,
        WorkTicks: workTicks,
        UnlockLevel: 3,
        Skill: CraftingSkill.Carpentry,
        SkillLevel: 2,
        Code: "saw");

    private static readonly CraftContext s_atBench = new(CraftingStation.Workbench, CharacterLevel: 5, SkillLevel: 5);

    [Fact]
    public void TryCraft_MultiOutput_ConsumesIngredientsAndAddsEveryOutput()
    {
        InventoryContainer bag = new(4);
        bag.TryAdd(new ItemSlot(Log, 3));

        CraftResult result = RecipeMatcher.TryCraft(bag, SawRecipe(), s_stacks, s_atBench);

        result.Should().Be(CraftResult.Ok);
        bag.CountItem(Log).Should().Be(2);
        bag.CountItem(Plank).Should().Be(4);
        bag.CountItem(Sawdust).Should().Be(2);
    }

    [Fact]
    public void TryCraft_WhenByproductDoesNotFit_ChangesNothing()
    {
        InventoryContainer bag = new(2);
        bag.TryAdd(new ItemSlot(Log, 5));
        bag.TryAdd(new ItemSlot(Sword, 1, InstanceId: 9));

        // Consuming one log keeps its slot occupied, so planks have no room and sawdust has none either.
        CraftResult result = RecipeMatcher.TryCraft(bag, SawRecipe(), s_stacks, s_atBench);

        result.Status.Should().Be(OpStatus.InventoryFull);
        result.Failure.Should().Be(CraftFailure.InventoryFull);
        bag.CountItem(Log).Should().Be(5, "ingredients must not be consumed when outputs cannot be placed");
        bag.CountItem(Plank).Should().Be(0);
    }

    [Fact]
    public void TryCraft_WhenConsumedIngredientFreesTheSlot_Succeeds()
    {
        InventoryContainer bag = new(2);
        bag.TryAdd(new ItemSlot(Log, 1));
        bag.TryAdd(new ItemSlot(Sword, 1, InstanceId: 9));
        CraftingRecipe single = SawRecipe() with { Byproducts = null };

        RecipeMatcher.TryCraft(bag, single, s_stacks, s_atBench).IsOk.Should().BeTrue();
        bag.CountItem(Plank).Should().Be(4);
        bag.CountItem(Log).Should().Be(0);
    }

    [Fact]
    public void TryCraft_WithFailedRequirements_ReturnsReasonAndChangesNothing()
    {
        InventoryContainer bag = new(4);
        bag.TryAdd(new ItemSlot(Log, 1));
        CraftingRecipe recipe = SawRecipe();

        RecipeMatcher.TryCraft(bag, recipe, s_stacks, s_atBench with { Station = CraftingStation.Forge }).Failure.Should().Be(CraftFailure.WrongStation);
        RecipeMatcher.TryCraft(bag, recipe, s_stacks, s_atBench with { CharacterLevel = 2 }).Failure.Should().Be(CraftFailure.LevelTooLow);
        RecipeMatcher.TryCraft(bag, recipe, s_stacks, s_atBench with { SkillLevel = 1 }).Failure.Should().Be(CraftFailure.SkillTooLow);
        CraftResult missing = RecipeMatcher.TryCraft(bag, recipe with { Ingredients = [new RecipeIngredient(Log, 2)] }, s_stacks, s_atBench);
        missing.Failure.Should().Be(CraftFailure.MissingIngredients);
        missing.Status.Should().Be(OpStatus.Conflict);

        bag.CountItem(Log).Should().Be(1);
    }

    [Fact]
    public void TryCraft_WithDuplicateIngredientLines_RequiresTheirSum()
    {
        InventoryContainer bag = new(4);
        bag.TryAdd(new ItemSlot(Log, 3));
        CraftingRecipe recipe = SawRecipe() with { Ingredients = [new RecipeIngredient(Log, 2), new RecipeIngredient(Log, 2)] };

        RecipeMatcher.TryCraft(bag, recipe, s_stacks, s_atBench).Failure.Should().Be(CraftFailure.MissingIngredients);
        bag.CountItem(Log).Should().Be(3);
    }

    [Fact]
    public void TryCraftInBag_NeverConsumesUpgradedGearOrTouchesEquipmentSlots()
    {
        InventoryContainer bag = new(Catalog.PlayerInventoryLayout.TotalCapacity);
        bag.SetSlot(0, new ItemSlot(Log, 1, UpgradeLevel: 3));
        CraftingRecipe recipe = SawRecipe() with { Byproducts = null };

        Catalog.PlayerInventoryLayout.TryCraftInBag(bag, recipe, 10).Should().BeFalse("an upgraded stack is not a crafting ingredient");
        bag.GetSlot(0).UpgradeLevel.Should().Be(3);

        for (int i = 1; i < Catalog.PlayerInventoryLayout.BagLimit; i++)
        {
            bag.SetSlot(i, new ItemSlot(Sword, 1, InstanceId: (ulong)i));
        }

        bag.SetSlot(1, new ItemSlot(Log, 2));
        Catalog.PlayerInventoryLayout.TryCraftInBag(bag, recipe, 10).Should().BeFalse("the bag is full and the equipment slots are off-limits");
        bag.CountItem(Log).Should().Be(3);
        bag.GetSlot(Catalog.PlayerInventoryLayout.HeadSlot).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Validate_DoesNotMutateInventory()
    {
        InventoryContainer bag = new(4);
        bag.TryAdd(new ItemSlot(Log, 1));

        RecipeMatcher.Validate(bag, SawRecipe(), s_stacks, s_atBench).IsOk.Should().BeTrue();
        bag.CountItem(Log).Should().Be(1);
        bag.CountItem(Plank).Should().Be(0);
    }
}
