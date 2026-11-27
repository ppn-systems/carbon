// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.IO;
using Carbon.Items.Definitions;
using Carbon.Survival.Components;
using Carbon.Survival.Stamina;
using FluentAssertions;
using Xunit;

namespace Carbon.Survival.Tests;

/// <summary>ADR-033 stamina rules, with concrete numbers from <see cref="s_testConfig"/>.</summary>
public sealed class StaminaMeterTests
{
    private const float Max = 100f;
    private const float Tick = 1f / 30f;

    /// <summary>Value of the sprint drain before ADR-033 (hardcoded 22/s in the server tick and the desktop).</summary>
    private const float PreviousSprintDrainPerSecond = 22f;

    private static readonly StaminaConfig s_testConfig = new()
    {
        SprintDrainPerSecond = 15f,
        RegenDelaySeconds = 1.5f,
        RegenPerSecondStill = 6f,
        RegenPerSecondMoving = 2f,
        RestingRegenMultiplier = 2f,
        ExhaustedRecoverFraction = 0.5f,
        MinimumRegenScale = 0.1f,
        FatigueRegenScale = new() { Tired = 0.5f, VeryTired = 0.25f, Exhausted = 0.2f },
        HungerRegenScale = new() { Hungry = 0.5f, UrgentlyHungry = 0.25f, Starving = 0.2f },
        SwingCosts = new() { BareHand = 3f, Melee = 7f, Tool = 9f, Ranged = 5f, Spell = 0f },
        ItemSwingCosts = new Dictionary<string, float> { ["heavy_axe"] = 11f },
        BuildWorkPerSecond = 6f,
    };

    [Fact]
    public void TrySpend_Affordable_DebitsExactlyTheCostAndStartsTheRegenDelay()
    {
        StaminaMeter meter = new(40f);

        meter.TrySpend(7f, s_testConfig).Should().BeTrue();

        meter.Current.Should().Be(33f);
        meter.RegenDelayRemaining.Should().Be(1.5f);
        meter.Exhausted.Should().BeFalse();
    }

    [Fact]
    public void TrySpend_MoreThanTheMeterHolds_RefusesWithoutDebitingAndExhausts()
    {
        StaminaMeter meter = new(6f);

        meter.TrySpend(7f, s_testConfig).Should().BeFalse();

        meter.Current.Should().Be(6f, "a refused action costs nothing");
        meter.Exhausted.Should().BeTrue("trying to act beyond the pool exhausts the body, so it cannot trickle-spam at low stamina");
        meter.CanAfford(3f).Should().BeFalse("an exhausted body refuses even cheap actions");
    }

    [Fact]
    public void TrySpend_ExactlyEmptiesTheMeter_ExhaustsTheBody()
    {
        StaminaMeter meter = new(7f);

        meter.TrySpend(7f, s_testConfig).Should().BeTrue();

        meter.Current.Should().Be(0f);
        meter.Exhausted.Should().BeTrue();
    }

    [Fact]
    public void Regenerate_BeforeTheDelayElapses_RestoresNothing()
    {
        StaminaMeter meter = new(50f);
        meter.TrySpend(10f, s_testConfig).Should().BeTrue();

        Step(ref meter, ticks: 44, rate: 6f); // 1.467 s < 1.5 s

        meter.Current.Should().Be(40f, "regen waits 1.5 s after the last spend");
    }

    [Fact]
    public void Regenerate_AfterTheDelay_RestoresAtTheRate()
    {
        StaminaMeter meter = new(50f);
        meter.TrySpend(10f, s_testConfig).Should().BeTrue();

        Step(ref meter, ticks: 45 + 30, rate: 6f); // 1.5 s delay, then 1 s of regen

        meter.Current.Should().BeApproximately(46f, 0.05f, "one second at 6/s after the delay");
    }

    [Fact]
    public void GetRegenPerSecond_Moving_IsSlowerThanStandingStill()
    {
        float still = s_testConfig.GetRegenPerSecond(new StaminaRegenContext(false, false, RestState.Rested, HungerState.Fed));
        float moving = s_testConfig.GetRegenPerSecond(new StaminaRegenContext(true, false, RestState.Rested, HungerState.Fed));
        float sitting = s_testConfig.GetRegenPerSecond(new StaminaRegenContext(false, true, RestState.Rested, HungerState.Fed));

        still.Should().Be(6f);
        moving.Should().Be(2f);
        sitting.Should().Be(12f, "sitting or sleeping doubles regen in the test config");
    }

    [Theory]
    [InlineData(RestState.Tired, HungerState.Fed, 3f)]
    [InlineData(RestState.Rested, HungerState.Starving, 1.2f)]
    [InlineData(RestState.Exhausted, HungerState.Starving, 0.6f)] // 0.2 * 0.2 = 0.04, floored to 0.1
    public void GetRegenPerSecond_TiredOrStarving_SlowsRegen(RestState fatigue, HungerState hunger, float expected) =>
        s_testConfig.GetRegenPerSecond(new StaminaRegenContext(false, false, fatigue, hunger)).Should().BeApproximately(expected, 0.0001f);

    [Fact]
    public void GetRegenPerSecond_StatusEffectScale_MultipliesTheRate()
    {
        s_testConfig.GetRegenPerSecond(new StaminaRegenContext(false, false, RestState.Rested, HungerState.Fed, EffectScale: 0.5f))
            .Should().Be(3f, "ADR-029 status effects plug in through EffectScale");
    }

    [Fact]
    public void TryDrain_SprintUntilEmpty_BlocksSprintUntilTheRecoverFraction()
    {
        StaminaMeter meter = new(15f);
        int sprintTicks = 0;
        while (meter.TryDrain(s_testConfig.SprintDrainPerSecond * Tick, s_testConfig))
        {
            sprintTicks++;
        }

        sprintTicks.Should().BeInRange(30, 31, "15 stamina at 15/s is one second of sprint");
        meter.Exhausted.Should().BeTrue();

        // Regen to just under half the pool: still exhausted, still no sprint.
        meter.Regenerate(1.5f, Max, 6f, s_testConfig);
        meter.Regenerate(49.9f / 6f, Max, 6f, s_testConfig);
        meter.Current.Should().BeApproximately(49.9f, 0.01f);
        meter.CanSprint.Should().BeFalse("exhaustion lasts until 50% in the test config");
        meter.TryDrain(0.5f, s_testConfig).Should().BeFalse();

        meter.Regenerate(0.2f / 6f, Max, 6f, s_testConfig);
        meter.Exhausted.Should().BeFalse();
        meter.CanSprint.Should().BeTrue();
    }

    [Fact]
    public void Refill_AfterExhaustion_ClearsTheLockAndTheDelay()
    {
        StaminaMeter meter = new(1f);
        _ = meter.TrySpend(5f, s_testConfig);

        meter.Refill(Max);

        meter.Current.Should().Be(Max);
        meter.Exhausted.Should().BeFalse();
        meter.RegenDelayRemaining.Should().Be(0f);
    }

    [Fact]
    public void GetSwingCost_PerItemAndPerKind_UsesTheOverrideThenTheKindDefault()
    {
        s_testConfig.GetSwingCost(null).Should().Be(3f, "bare hand");
        s_testConfig.GetSwingCost(Item("copper_sword", ItemCategory.Weapon, ToolCapabilities.MeleeWeapon)).Should().Be(7f);
        s_testConfig.GetSwingCost(Item("wood_axe", ItemCategory.Tool, ToolCapabilities.ChopFlora)).Should().Be(9f);
        s_testConfig.GetSwingCost(Item("heavy_axe", ItemCategory.Tool, ToolCapabilities.ChopFlora)).Should().Be(11f, "itemSwingCosts overrides the tool default");
        s_testConfig.GetSwingCost(Item("hunting_bow", ItemCategory.Weapon, ToolCapabilities.RangedBow)).Should().Be(5f);
        s_testConfig.GetSwingCost(Item("arcane_staff", ItemCategory.Weapon, ToolCapabilities.MagicFocus)).Should().Be(0f, "staffs are paid with mana");
        s_testConfig.GetSwingCost(Item("wood_log", ItemCategory.Resource, ToolCapabilities.None)).Should().Be(3f, "holding a material swings bare-handed");
    }

    [Fact]
    public void LoadFromJson_RepositoryStaminaJson_PinsThePlayableTuning()
    {
        StaminaConfig config = StaminaConfig.LoadFromJson(File.ReadAllText(SurvivalTestWorld.FindConfigFile("stamina.json")));

        config.SprintDrainPerSecond.Should().Be(12f, "the ADR-033 amendment pins the sprint drain");
        config.SprintDrainPerSecond.Should().BeLessThan(PreviousSprintDrainPerSecond, "running costs less than before ADR-033");
        config.RegenDelaySeconds.Should().Be(1f);
        config.ExhaustedRecoverFraction.Should().Be(0.25f);
        config.RegenPerSecondMoving.Should().BeLessThan(config.RegenPerSecondStill);
        config.ItemSwingCosts["wood_axe"].Should().Be(4f);
        config.MinimumRegenScale.Should().BeGreaterThan(0f, "regen never stops, so a fresh character cannot soft-lock");
    }

    [Fact]
    public void LoadFromJson_MovingRegenFasterThanStill_IsRejected()
    {
        string json = File.ReadAllText(SurvivalTestWorld.FindConfigFile("stamina.json"))
            .Replace("\"regenPerSecondMoving\": 8", "\"regenPerSecondMoving\": 30", System.StringComparison.Ordinal);

        FluentActions.Invoking(() => StaminaConfig.LoadFromJson(json)).Should().Throw<InvalidDataException>().WithMessage("*regenPerSecondMoving*");
    }

    private static ItemArchetype Item(string code, ItemCategory category, ToolCapabilities capabilities) =>
        new(1U, code, category, Capabilities: capabilities);

    private static void Step(ref StaminaMeter meter, int ticks, float rate)
    {
        for (int i = 0; i < ticks; i++)
        {
            meter.Regenerate(Tick, Max, rate, s_testConfig);
        }
    }
}
