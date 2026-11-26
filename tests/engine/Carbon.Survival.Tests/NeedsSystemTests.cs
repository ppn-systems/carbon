// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Combat.Events;
using Carbon.Simulation.ECS;
using Carbon.Survival.Components;
using Carbon.Survival.Config;
using Carbon.Survival.Events;
using Carbon.Survival.Systems;
using FluentAssertions;
using Xunit;

namespace Carbon.Survival.Tests;

public sealed class NeedsSystemTests
{
    [Fact]
    public void Update_WhenWalkingFromFull_ReachesStarvingAfterSixtyMinutes()
    {
        SurvivalTestWorld sim = new();
        Entity player = sim.SpawnPlayer();
        NeedsSystem.SetActivity(sim.World, player, NeedsActivity.Walking).Should().BeTrue();

        sim.StepSeconds((60 * 60) - 10);
        sim.Needs(player).Hunger.Should().Be(HungerState.UrgentlyHungry, "10 s of food remain at the walking rate");

        sim.StepSeconds(20);
        sim.Needs(player).Hunger.Should().Be(HungerState.Starving, "walking drains 100 food in 60 minutes");
        sim.Needs(player).Food.Should().Be(0f);
    }

    [Theory]
    [InlineData(NeedsActivity.Idle, 0.5f)]
    [InlineData(NeedsActivity.Walking, 1.0f)]
    [InlineData(NeedsActivity.Sprinting, 2.0f)]
    [InlineData(NeedsActivity.Fighting, 1.75f)]
    [InlineData(NeedsActivity.Swimming, 2.5f)]
    [InlineData(NeedsActivity.Resting, 0.4f)]
    public void Update_PerActivity_DrainsFoodAtConfiguredMultiplier(NeedsActivity activity, float multiplier)
    {
        SurvivalTestWorld sim = new();
        Entity player = sim.SpawnPlayer();
        sim.Needs(player).Activity = activity;

        sim.StepSeconds(10 * 60);

        float expectedDrain = sim.Config.FoodDecayPerMinute * multiplier * 10f;
        sim.Needs(player).Food.Should().BeApproximately(100f - expectedDrain, 0.05f, $"{activity} drains at x{multiplier}");
    }

    [Fact]
    public void Update_WhileStarving_CrossesEveryThresholdInOrderWithEffects()
    {
        SurvivalTestWorld sim = new();
        Entity player = sim.SpawnPlayer();
        sim.Needs(player).Activity = NeedsActivity.Walking;

        // 75 % consumed at 45 min → Hungry.
        sim.StepSeconds((45 * 60) + 5);
        NeedsComponent hungry = sim.Needs(player);
        hungry.Hunger.Should().Be(HungerState.Hungry);
        hungry.Mood.Should().BeApproximately(sim.Config.MoodBaseline + sim.Config.HungryMood, 0.001f, "hungry is a minor mood penalty");
        hungry.MoveSpeedScale.Should().Be(1f, "hungry has no speed penalty");
        hungry.HealthRegenBlocked.Should().BeFalse();

        // 87.5 % consumed at 52.5 min → UrgentlyHungry.
        sim.StepSeconds((int)(7.5f * 60));
        NeedsComponent urgent = sim.Needs(player);
        urgent.Hunger.Should().Be(HungerState.UrgentlyHungry);
        urgent.MoveSpeedScale.Should().Be(sim.Config.UrgentlyHungrySpeedScale);
        urgent.AttackSpeedScale.Should().Be(sim.Config.UrgentlyHungrySpeedScale);
        urgent.HealthRegenBlocked.Should().BeTrue("urgently hungry stops health regen");

        sim.StepSeconds((int)(7.5f * 60));
        NeedsComponent starving = sim.Needs(player);
        starving.Hunger.Should().Be(HungerState.Starving);
        starving.MoveSpeedScale.Should().Be(sim.Config.StarvingSpeedScale);
        starving.Malnutrition.Should().BeGreaterThan(0f);

        List<(HungerState From, HungerState To)> transitions = [];
        foreach (NeedStateChangedEvent e in sim.World.ReadEvents<NeedStateChangedEvent>())
        {
            if (e.Need == NeedKind.Food)
            {
                e.Entity.Should().Be(player);
                transitions.Add(((HungerState)e.Previous, (HungerState)e.Current));
            }
        }

        transitions.Should().Equal(
            (HungerState.Fed, HungerState.Hungry),
            (HungerState.Hungry, HungerState.UrgentlyHungry),
            (HungerState.UrgentlyHungry, HungerState.Starving));
    }

    [Fact]
    public void Update_WhenStarvingFromEmpty_DamagesEveryFiveSecondsAndKillsAtTenMinutes()
    {
        SurvivalTestWorld sim = new();
        Entity player = sim.SpawnPlayer(food: 0f, health: 100f);

        sim.StepSeconds(60);
        sim.World.ReadEvents<DamageEvent>().Length.Should().Be(12, "one starvation pulse every 5 s");
        sim.Needs(player).Malnutrition.Should().BeApproximately(0.1f, 0.001f);

        sim.StepSeconds(530);
        sim.Health(player).IsDead.Should().BeFalse("malnutrition is not yet at maximum after 9:50");
        sim.Health(player).Current.Should().BeLessThan(20f, "pulses scale with malnutrition severity");

        sim.StepSeconds(20);
        sim.Health(player).IsDead.Should().BeTrue("maximum malnutrition at 10:00 is lethal");
        ReadOnlySpan<EntityKilledEvent> kills = sim.World.ReadEvents<EntityKilledEvent>();
        kills.Length.Should().Be(1);
        kills[0].Victim.Should().Be(player);
        kills[0].Killer.Should().Be(player, "starvation is self-inflicted");

        int damageEvents = sim.World.ReadEvents<DamageEvent>().Length;
        sim.StepSeconds(60);
        sim.World.ReadEvents<DamageEvent>().Length.Should().Be(damageEvents, "dead entities are no longer processed");
    }

    [Fact]
    public void Update_WhenFedAfterStarving_RecoversMalnutritionAndStopsDamage()
    {
        SurvivalTestWorld sim = new();
        Entity player = sim.SpawnPlayer(food: 0f);
        sim.StepSeconds(120);
        sim.Needs(player).Malnutrition.Should().BeApproximately(0.2f, 0.001f);

        sim.Needs(player).Food = 80f;
        sim.StepSeconds(1);
        sim.Needs(player).Hunger.Should().Be(HungerState.Fed);
        float health = sim.Health(player).Current;
        int damage = sim.World.ReadEvents<DamageEvent>().Length;

        sim.StepSeconds(4 * 60);
        sim.Needs(player).Malnutrition.Should().Be(0f, "malnutrition recovers at 0.05 per minute once fed");
        sim.Health(player).Current.Should().Be(health);
        sim.World.ReadEvents<DamageEvent>().Length.Should().Be(damage, "no starvation damage once fed");
        sim.Needs(player).HealthRegenBlocked.Should().BeFalse();
    }

    [Fact]
    public void Update_RestDrainsWhileAwakeAndRecoversWhileResting()
    {
        SurvivalTestWorld sim = new(new NeedsConfig { FoodDecayPerMinute = 0.001f });
        Entity player = sim.SpawnPlayer();
        sim.Needs(player).Activity = NeedsActivity.Walking;

        // Tired below 28 % → 72 % of 120 min = 86.4 min.
        sim.StepSeconds(86 * 60);
        sim.Needs(player).Fatigue.Should().Be(RestState.Rested);
        sim.StepSeconds(60);
        sim.Needs(player).Fatigue.Should().Be(RestState.Tired);
        sim.Needs(player).Mood.Should().BeApproximately(sim.Config.MoodBaseline + sim.Config.TiredMood, 0.001f);

        sim.Needs(player).Activity = NeedsActivity.Resting;
        sim.StepSeconds(60);
        sim.Needs(player).Fatigue.Should().Be(RestState.Rested, "sitting recovers 3 rest per minute (30 % of the bed rate)");
    }

    [Theory]
    [InlineData(NeedsActivity.Resting, false, 3f)]
    [InlineData(NeedsActivity.Sleeping, false, 10f)]
    [InlineData(NeedsActivity.Sleeping, true, 15f)]
    public void Update_WhileResting_RecoversAtSitBedOrComfortRate(NeedsActivity activity, bool comfort, float perMinute)
    {
        SurvivalTestWorld sim = new(new NeedsConfig { FoodDecayPerMinute = 0.001f });
        Entity player = sim.SpawnPlayer();
        sim.Needs(player).Rest = 20f;
        sim.Needs(player).Activity = activity;
        sim.Needs(player).SleepComfort = comfort;

        sim.StepSeconds(60);

        sim.Needs(player).Rest.Should().BeApproximately(20f + perMinute, 0.05f, $"{activity} (comfort {comfort}) recovers {perMinute}/min");
    }

    [Fact]
    public void Update_WhenVeryTiredOrExhausted_SlowsAndExhaustionBlocksRegen()
    {
        SurvivalTestWorld sim = new(new NeedsConfig { FoodDecayPerMinute = 0.001f });
        Entity drowsy = sim.SpawnPlayer();
        Entity spent = sim.SpawnPlayer();
        sim.Needs(drowsy).Rest = 10f;
        sim.Needs(spent).Rest = 0f;

        sim.StepSeconds(1);

        sim.Needs(drowsy).Fatigue.Should().Be(RestState.VeryTired);
        sim.Needs(drowsy).MoveSpeedScale.Should().Be(sim.Config.VeryTiredSpeedScale);
        sim.Needs(drowsy).HealthRegenBlocked.Should().BeFalse();
        sim.Needs(spent).Fatigue.Should().Be(RestState.Exhausted);
        sim.Needs(spent).MoveSpeedScale.Should().Be(sim.Config.ExhaustedSpeedScale);
        sim.Needs(spent).AttackSpeedScale.Should().Be(sim.Config.ExhaustedSpeedScale);
        sim.Needs(spent).HealthRegenBlocked.Should().BeTrue("exhaustion stops health regen");
    }

    [Fact]
    public void Update_WhileSleeping_FoodDrainsAtTheRestingRate()
    {
        SurvivalTestWorld sim = new();
        Entity sleeper = sim.SpawnPlayer();
        sim.Needs(sleeper).Activity = NeedsActivity.Sleeping;

        sim.StepSeconds(10 * 60);

        float expected = 100f - (sim.Config.FoodDecayPerMinute * sim.Config.FoodActivity.Resting * 10f);
        sim.Needs(sleeper).Food.Should().BeApproximately(expected, 0.05f, "sleeping burns food like resting");
    }

    [Fact]
    public void Update_WhenMoodCrossesBand_PublishesMoodStateChange()
    {
        SurvivalTestWorld sim = new();
        Entity player = sim.SpawnPlayer(food: 0f);

        sim.StepSeconds(1);

        bool found = false;
        foreach (NeedStateChangedEvent e in sim.World.ReadEvents<NeedStateChangedEvent>())
        {
            if (e.Need == NeedKind.Mood)
            {
                found = true;
                e.Entity.Should().Be(player);
                ((MoodState)e.Previous).Should().Be(MoodState.Neutral);
                ((MoodState)e.Current).Should().Be(MoodState.Stressed, "50 - 20 starving = 30 < 35");
                e.Level.Should().BeApproximately(30f, 0.001f);
            }
        }

        found.Should().BeTrue("the HUD needs a mood band change notification");
    }
}
