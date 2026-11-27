// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Contracts.Common;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Survival.Components;
using Carbon.Survival.Config;
using Carbon.Survival.Events;
using Carbon.Survival.Food;
using Carbon.Survival.Systems;
using FluentAssertions;
using Xunit;

namespace Carbon.Survival.Tests;

public sealed class EatActionSystemTests
{
    private const int StewTicks = 6 * SurvivalTestWorld.TickRate;

    [Fact]
    public void BeginEat_WhenMealDurationElapses_AppliesNutritionOnce()
    {
        SurvivalTestWorld sim = new();
        Entity player = sim.SpawnPlayer(food: 20f);

        sim.Eating.BeginEat(sim.World, player, "stew").Should().Be(OpStatus.Ok);
        sim.StepTicks(StewTicks - 1);
        EatActionSystem.IsEating(sim.World, player).Should().BeTrue("stew takes 6 s");
        sim.Needs(player).Food.Should().BeLessThan(20f, "no nutrition before the meal finishes");
        sim.World.ReadEvents<MealCompletedEvent>().Length.Should().Be(0);

        sim.StepTicks(1);
        EatActionSystem.IsEating(sim.World, player).Should().BeFalse();
        ReadOnlySpan<MealCompletedEvent> meals = sim.World.ReadEvents<MealCompletedEvent>();
        meals.Length.Should().Be(1);
        meals[0].Entity.Should().Be(player);
        meals[0].NutritionGained.Should().Be(60f);
        sim.Catalog.Get(meals[0].FoodIndex).ItemId.Should().Be("stew");
        sim.Needs(player).Food.Should().BeApproximately(80f, 0.2f);
    }

    [Fact]
    public void BeginEat_WhenAlmostFull_CapsNutritionAtMaximum()
    {
        SurvivalTestWorld sim = new();
        Entity player = sim.SpawnPlayer(food: 85f);

        sim.Eating.BeginEat(sim.World, player, "stew").Should().Be(OpStatus.Ok);
        sim.StepTicks(StewTicks);

        sim.Needs(player).Food.Should().BeApproximately(sim.Config.FoodMax, 0.01f, "excess nutrition is wasted, not stored");
        sim.World.ReadEvents<MealCompletedEvent>()[0].NutritionGained.Should().BeLessThan(15.1f);
    }

    [Fact]
    public void BeginEat_InvalidRequests_ReturnSpecificStatus()
    {
        SurvivalTestWorld sim = new();
        Entity hungry = sim.SpawnPlayer(food: 10f);
        Entity full = sim.SpawnPlayer(food: 95f);
        Entity dead = sim.SpawnPlayer(food: 10f, health: 0f);
        Entity noNeeds = sim.World.SpawnEntity(Vector2.Zero);

        sim.Eating.BeginEat(sim.World, hungry, string.Empty).Should().Be(OpStatus.Invalid);
        sim.Eating.BeginEat(sim.World, hungry, "iron_ore").Should().Be(OpStatus.NotFound, "ore is not edible");
        sim.Eating.BeginEat(sim.World, noNeeds, "bread").Should().Be(OpStatus.Invalid);
        sim.Eating.BeginEat(sim.World, dead, "bread").Should().Be(OpStatus.Forbidden);
        sim.Eating.BeginEat(sim.World, full, "bread").Should().Be(OpStatus.Conflict, "95 % is above the 90 % overeating cap");
        sim.Eating.BeginEat(sim.World, new Entity(999, 1), "bread").Should().Be(OpStatus.NotFound);

        sim.Eating.BeginEat(sim.World, hungry, "bread").Should().Be(OpStatus.Ok);
        sim.Eating.BeginEat(sim.World, hungry, "stew").Should().Be(OpStatus.Conflict, "already eating");
    }

    [Fact]
    public void Update_WhenEaterTakesDamage_InterruptsWithoutNutrition()
    {
        SurvivalTestWorld sim = new();
        Entity player = sim.SpawnPlayer(food: 20f);
        sim.Eating.BeginEat(sim.World, player, "stew").Should().Be(OpStatus.Ok);
        sim.StepTicks(30);

        ref HealthComponent health = ref sim.Health(player);
        health = new HealthComponent(health.Current - 5f, health.Max);
        sim.StepTicks(1);

        EatActionSystem.IsEating(sim.World, player).Should().BeFalse("damage interrupts eating");
        ReadOnlySpan<MealInterruptedEvent> interrupts = sim.World.ReadEvents<MealInterruptedEvent>();
        interrupts.Length.Should().Be(1);
        interrupts[0].Reason.Should().Be(MealInterruptReason.Damaged);

        sim.StepTicks(StewTicks);
        sim.World.ReadEvents<MealCompletedEvent>().Length.Should().Be(0);
        sim.Needs(player).Food.Should().BeLessThan(20f);
    }

    [Fact]
    public void Update_WhenStarvationDamageLandsMidMeal_DoesNotInterrupt()
    {
        SurvivalTestWorld sim = new();
        Entity player = sim.SpawnPlayer(food: 0f);
        sim.Needs(player).StarvationTimer = sim.Config.StarvationDamageIntervalSeconds - 0.05f;

        sim.Eating.BeginEat(sim.World, player, "bread").Should().Be(OpStatus.Ok);
        sim.StepTicks(3 * SurvivalTestWorld.TickRate);

        sim.World.ReadEvents<Carbon.Combat.Events.DamageEvent>().Length.Should().BeGreaterThan(0, "a starvation pulse landed during the meal");
        sim.World.ReadEvents<MealInterruptedEvent>().Length.Should().Be(0, "starvation must not block the meal that cures it");
        sim.World.ReadEvents<MealCompletedEvent>().Length.Should().Be(1);
    }

    [Fact]
    public void CancelEat_WhenEating_StopsMealAndPublishesCancelled()
    {
        SurvivalTestWorld sim = new();
        Entity player = sim.SpawnPlayer(food: 20f);
        EatActionSystem.CancelEat(sim.World, player).Should().Be(OpStatus.NotFound);

        sim.Eating.BeginEat(sim.World, player, "bread").Should().Be(OpStatus.Ok);
        EatActionSystem.CancelEat(sim.World, player).Should().Be(OpStatus.Ok);

        EatActionSystem.IsEating(sim.World, player).Should().BeFalse();
        sim.World.ReadEvents<MealInterruptedEvent>()[0].Reason.Should().Be(MealInterruptReason.Cancelled);
    }

    [Fact]
    public void Complete_CookedMealRaisesMoodAndRawFoodLowersItUntilMemoryFades()
    {
        SurvivalTestWorld sim = new();
        Entity gourmet = sim.SpawnPlayer(food: 30f);
        Entity forager = sim.SpawnPlayer(food: 30f);

        sim.Eating.BeginEat(sim.World, gourmet, "stew").Should().Be(OpStatus.Ok);
        sim.Eating.BeginEat(sim.World, forager, "raw_meat").Should().Be(OpStatus.Ok);
        sim.StepTicks(StewTicks + 1);

        sim.Needs(gourmet).Mood.Should().BeApproximately(sim.Config.MoodBaseline + 5f, 0.001f, "fine meal +5");
        sim.Needs(gourmet).GetMemory(0).Thought.Should().Be(MoodThought.AteFineMeal);
        sim.Needs(forager).Mood.Should().BeApproximately(sim.Config.MoodBaseline - 7f, 0.001f, "ate raw food -7");

        sim.StepSeconds(900);
        sim.Needs(gourmet).Mood.Should().BeApproximately(sim.Config.MoodBaseline, 0.001f, "the 15-minute memory has faded");
        sim.Needs(gourmet).GetMemory(0).IsActive.Should().BeFalse();
    }

    [Fact]
    public void Complete_WhenPoisonChanceIsCertain_PoisonsWithEffects()
    {
        NeedsConfig config = new();
        SurvivalTestWorld sim = new(config, SurvivalTestWorld.CreateCatalog(config, rawPoisonChance: 1f));
        Entity player = sim.SpawnPlayer(food: 50f);

        sim.Eating.BeginEat(sim.World, player, "raw_meat").Should().Be(OpStatus.Ok);
        sim.StepTicks((4 * SurvivalTestWorld.TickRate) + 1);

        sim.World.ReadEvents<MealCompletedEvent>()[0].Poisoned.Should().BeTrue();
        NeedsComponent needs = sim.Needs(player);
        needs.IsPoisoned.Should().BeTrue();
        needs.HealthRegenBlocked.Should().BeTrue("food poisoning blocks regen");
        needs.MoveSpeedScale.Should().Be(config.PoisonSpeedScale);
        needs.Mood.Should().BeApproximately(config.MoodBaseline + config.PoisonMood - 7f, 0.001f);

        sim.StepSeconds((int)config.PoisonDurationSeconds);
        sim.Needs(player).IsPoisoned.Should().BeFalse("poisoning wears off");
    }

    [Fact]
    public void FoodPoisoningRoll_SameInputs_IsDeterministicAndUniform()
    {
        Entity eater = new(7, 3);
        FoodPoisoningRoll.Sample(TestGameplayKeys.Key(42), eater, 1000, 2).Should().Be(FoodPoisoningRoll.Sample(TestGameplayKeys.Key(42), eater, 1000, 2));
        FoodPoisoningRoll.Sample(TestGameplayKeys.Key(42), eater, 1000, 2).Should().NotBe(FoodPoisoningRoll.Sample(TestGameplayKeys.Key(43), eater, 1000, 2));

        int below = 0;
        const int Samples = 20_000;
        for (ulong tick = 0; tick < Samples; tick++)
        {
            double sample = FoodPoisoningRoll.Sample(TestGameplayKeys.Key(42), eater, tick, 1);
            sample.Should().BeInRange(0.0, 1.0);
            if (sample < 0.15)
            {
                below++;
            }
        }

        ((double)below / Samples).Should().BeApproximately(0.15, 0.01, "raw meat's 15 % chance should hold over many meals");
    }

    [Fact]
    public void Complete_SameSeedInTwoWorlds_ProducesIdenticalPoisoningSequence()
    {
        static List<bool> Run(ulong seed)
        {
            NeedsConfig config = new();
            SurvivalTestWorld sim = new(config, SurvivalTestWorld.CreateCatalog(config, rawPoisonChance: 0.5f), seed);
            Entity player = sim.SpawnPlayer(food: 10f);
            List<bool> outcomes = [];
            for (int meal = 0; meal < 40; meal++)
            {
                sim.Needs(player).Food = 10f;
                sim.Eating.BeginEat(sim.World, player, "raw_meat").Should().Be(OpStatus.Ok);
                sim.StepTicks(4 * SurvivalTestWorld.TickRate);
            }

            foreach (MealCompletedEvent e in sim.World.ReadEvents<MealCompletedEvent>())
            {
                outcomes.Add(e.Poisoned);
            }

            return outcomes;
        }

        List<bool> first = Run(1234);
        first.Should().HaveCount(40);
        Run(1234).Should().Equal(first, "the poisoning roll is a pure function of key, eater, tick and food");
        first.Should().Contain(true).And.Contain(false);
    }
}
