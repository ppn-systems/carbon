// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Contracts.Common;
using Carbon.Items.Components;
using Carbon.Items.Containers;
using Carbon.Items.Crafting;
using Carbon.Items.Events;
using Carbon.Items.Systems;
using Carbon.Simulation.Clock;
using Carbon.Simulation.ECS;
using Carbon.Simulation.World;
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
    public void Validate_DoesNotMutateInventory()
    {
        InventoryContainer bag = new(4);
        bag.TryAdd(new ItemSlot(Log, 1));

        RecipeMatcher.Validate(bag, SawRecipe(), s_stacks, s_atBench).IsOk.Should().BeTrue();
        bag.CountItem(Log).Should().Be(1);
        bag.CountItem(Plank).Should().Be(0);
    }

    [Fact]
    public void CraftingJob_ProgressesPerTickAndDeliversOutputsAllOrNothing()
    {
        WorldSimulation world = new();
        world.RegisterSystem(new CraftingJobSystem());
        Entity crafter = world.SpawnEntity(Vector2.Zero);
        CraftingRecipe recipe = SawRecipe(workTicks: 10);
        InventoryContainer bag = new(3);
        bag.TryAdd(new ItemSlot(Log, 1));

        RecipeMatcher.TryConsumeIngredients(bag, recipe, s_stacks, s_atBench).IsOk.Should().BeTrue();
        bag.CountItem(Log).Should().Be(0, "ingredients are reserved when the job starts");
        CraftingJobComponent job = CraftingJobComponent.Start(recipe, skillLevel: 0);
        world.Entities.GetPool<CraftingJobComponent>().Set(crafter.Id, job);

        for (ulong tick = 1; tick <= 9; tick++)
        {
            world.Tick(new FixedTickArgs(tick, 1f / 30f, tick / 30d));
        }

        ComponentPool<CraftingJobComponent> pool = world.Entities.GetPool<CraftingJobComponent>();
        pool.Get(crafter.Id).State.Should().Be(CraftingJobState.Running);
        pool.Get(crafter.Id).Progress.Should().BeApproximately(0.9f, 0.001f);
        world.ReadEvents<CraftingJobCompletedEvent>().Length.Should().Be(0);

        world.Tick(new FixedTickArgs(10, 1f / 30f, 10 / 30d));
        pool.Get(crafter.Id).State.Should().Be(CraftingJobState.Completed);
        world.ReadEvents<CraftingJobCompletedEvent>().ToArray().Should().ContainSingle()
            .Which.Should().Be(new CraftingJobCompletedEvent(crafter, recipe.Id));

        // Inventory filled up meanwhile: delivery must wait without losing anything.
        bag.TryAdd(new ItemSlot(Sword, 1, InstanceId: 1));
        bag.TryAdd(new ItemSlot(Sword, 1, InstanceId: 2));
        bag.TryAdd(new ItemSlot(Sword, 1, InstanceId: 3));
        RecipeMatcher.TryDeliverOutputs(bag, recipe, s_stacks).Failure.Should().Be(CraftFailure.InventoryFull);
        bag.CountItem(Plank).Should().Be(0);

        bag.TryRemove(Sword, 2);
        RecipeMatcher.TryDeliverOutputs(bag, recipe, s_stacks).IsOk.Should().BeTrue();
        bag.CountItem(Plank).Should().Be(4);
        bag.CountItem(Sawdust).Should().Be(2);
    }

    [Fact]
    public void CraftingWork_HigherSkill_FinishesFaster()
    {
        CraftingRecipe recipe = SawRecipe(workTicks: 300);
        CraftingJobComponent novice = CraftingJobComponent.Start(recipe, 0);
        CraftingJobComponent master = CraftingJobComponent.Start(recipe, 50);

        int noviceTicks = TicksToFinish(novice);
        int masterTicks = TicksToFinish(master);

        noviceTicks.Should().Be(300);
        masterTicks.Should().Be(100, "skill 50 works at 3x baseline");
        CraftingJobComponent.Start(recipe with { WorkTicks = 0 }, 0).State.Should().Be(CraftingJobState.Completed);
    }

    private static int TicksToFinish(CraftingJobComponent job)
    {
        int ticks = 0;
        while (job.State == CraftingJobState.Running)
        {
            job = job.Advance();
            ticks++;
        }

        return ticks;
    }
}
