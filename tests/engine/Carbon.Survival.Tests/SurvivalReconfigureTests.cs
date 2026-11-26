// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Contracts.Common;
using Carbon.Simulation.ECS;
using Carbon.Survival.Events;
using Carbon.Survival.Food;
using Carbon.Survival.Systems;
using FluentAssertions;
using Xunit;

namespace Carbon.Survival.Tests;

/// <summary>Config hot reload swaps the survival tables without completing a meal against the wrong food.</summary>
public sealed class SurvivalReconfigureTests
{
    private const int StewTicks = 6 * SurvivalTestWorld.TickRate;

    [Fact]
    public void Reconfigure_MealInProgressWhoseFoodMovedIndex_CompletesAgainstSameFoodWithNewNutrition()
    {
        SurvivalTestWorld sim = new();
        Entity player = sim.SpawnPlayer(food: 10f);
        sim.Eating.BeginEat(sim.World, player, "stew").Should().Be(OpStatus.Ok);
        sim.StepTicks(10);

        // "stew" moves from index 2 to index 0 and becomes more nutritious.
        FoodCatalog reordered = new(
            [
                new FoodDefinition { ItemId = "stew", Nutrition = 80f, Quality = FoodQuality.Fine, EatSeconds = 6f },
                new FoodDefinition { ItemId = "bread", Nutrition = 35f, Quality = FoodQuality.Simple, EatSeconds = 3f },
            ],
            sim.Config.FoodMax);
        sim.Eating.Reconfigure(sim.World, reordered, sim.Config);
        sim.StepTicks(StewTicks - 10);

        ReadOnlySpan<MealCompletedEvent> meals = sim.World.ReadEvents<MealCompletedEvent>();
        meals.Length.Should().Be(1);
        reordered.Get(meals[0].FoodIndex).ItemId.Should().Be("stew");
        meals[0].NutritionGained.Should().Be(80f);
    }

    [Fact]
    public void Reconfigure_MealInProgressWhoseFoodWasRemoved_IsCancelledWithoutNutrition()
    {
        SurvivalTestWorld sim = new();
        Entity player = sim.SpawnPlayer(food: 10f);
        sim.Eating.BeginEat(sim.World, player, "stew").Should().Be(OpStatus.Ok);
        float before = sim.Needs(player).Food;

        FoodCatalog withoutStew = new([new FoodDefinition { ItemId = "bread", Nutrition = 35f, EatSeconds = 3f }], sim.Config.FoodMax);
        sim.Eating.Reconfigure(sim.World, withoutStew, sim.Config);

        EatActionSystem.IsEating(sim.World, player).Should().BeFalse();
        ReadOnlySpan<MealInterruptedEvent> interrupts = sim.World.ReadEvents<MealInterruptedEvent>();
        interrupts.Length.Should().Be(1);
        interrupts[0].Reason.Should().Be(MealInterruptReason.Cancelled);
        sim.StepTicks(StewTicks);
        sim.World.ReadEvents<MealCompletedEvent>().Length.Should().Be(0);
        sim.Needs(player).Food.Should().BeLessThanOrEqualTo(before);
        sim.Eating.BeginEat(sim.World, player, "stew").Should().Be(OpStatus.NotFound, "the new catalog no longer has stew");
    }
}
