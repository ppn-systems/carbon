// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Abstractions.World;
using Carbon.Contracts.Common;
using Carbon.Items.Catalog;
using Carbon.Survival.Stamina;
using FluentAssertions;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>
/// ADR-033 amendment: stamina on the SHIPPED config is playable for a fresh hero. Each test plays the activity in a real
/// world and pins the outcome the owner asked for; none re-derives the tuning formula.
/// </summary>
public sealed class StaminaPacingTests
{
    private const uint WoodSword = 189;
    private static readonly HeroBuild s_levelOne = new(1, 0, 0, WoodSword, StarterGameCatalog.WoodAxe);

    [Fact]
    public void Fight_LevelOneHeroWithWoodSword_KillsALevelOneSlimeWithoutBeingExhausted()
    {
        StaminaPacingWorld world = StaminaPacingWorld.CreateNextToMonster(s_levelOne, StaminaPacingWorld.WildSlimeArchetype, level: 1, out EntitySpawnSnapshot slime);

        FightResult fight = world.Fight(slime);

        fight.Killed.Should().BeTrue($"a level-1 hero beats a level-1 slime ({fight})");
        fight.Exhaustions.Should().Be(0, "a normal same-level fight never empties the bar");
        fight.MinStamina.Should().BeGreaterThan(0f);
    }

    [Fact]
    public void ChopOak_LevelOneHeroWithWoodAxe_FellsItWithAtMostOneRest()
    {
        StaminaPacingWorld world = StaminaPacingWorld.Create(s_levelOne);

        ChopResult chop = world.ChopOak();

        chop.Felled.Should().BeTrue("the oak falls to the wood axe");
        chop.Rests.Should().BeLessThanOrEqualTo(1, "a level-1 hero chops a tree stopping at most once");
    }

    [Fact]
    public void SwingUntilRefused_LevelOneHeroWithWoodSword_HasAFightsWorthOfSwings()
    {
        StaminaPacingWorld world = StaminaPacingWorld.CreateAtBoulder(s_levelOne);

        int swings = world.SwingUntilRefused();

        // A slime takes 5 hits; 20 covers a slime pack and some misses (the hardcore tuning gave 16).
        swings.Should().BeGreaterThanOrEqualTo(20, "a full level-1 bar lasts a whole fight");
    }

    [Fact]
    public void RestFromExhaustion_LevelOneHero_ActsAgainAfterAShortBreather()
    {
        StaminaPacingWorld world = StaminaPacingWorld.CreateAtBoulder(s_levelOne);
        _ = world.SwingUntilRefused();
        world.IsExhausted.Should().BeTrue();

        (float unlocked, float full) = world.RestFromExhaustion();

        // The hardcore tuning took 6.5 s to unlock and 17.4 s to refill.
        unlocked.Should().BeInRange(0.5f, 4f, "exhaustion is a breather, not a long wait");
        full.Should().BeLessThanOrEqualTo(10f, "standing still refills a level-1 bar in seconds");
    }

    [Fact]
    public void SwingAxeAtOak_CostAboveCurrentStamina_IsRefusedAndTheOakIsUntouched()
    {
        float cost = StaminaConfig.LoadShippedDefaults().ItemSwingCosts["wood_axe"];
        StaminaPacingWorld world = StaminaPacingWorld.Create(s_levelOne, stamina: cost - 1f);

        world.SwingAxeAtOak().Should().Be(OpStatus.RateLimited, "a swing the hero cannot afford is refused");

        world.OakDamaged().Should().BeFalse("a refused swing does not touch the oak");
        world.Stamina().Should().Be(cost - 1f, "a refused swing costs nothing");
        world.Idle(1);
        world.Refusals.Should().Be(1U, "the client is told the swing was refused");
    }

    [Fact]
    public void Attack_WhiffWithNothingInReach_CostsNothingAndStillStartsTheCooldown()
    {
        StaminaPacingWorld world = StaminaPacingWorld.CreateAtBoulder(s_levelOne);
        float full = world.Stamina();

        world.Whiff();
        world.Idle(1);
        world.Stamina().Should().Be(full, "swinging at the air is free");

        world.SwingAtBoulder();
        world.Idle(1);
        world.Stamina().Should().Be(full, "the whiff started the cooldown, so the next swing does not happen yet");
    }

    [Fact]
    public void Attack_SwingThatReachesAProp_CostsTheConfiguredSwingCost()
    {
        StaminaPacingWorld world = StaminaPacingWorld.CreateAtBoulder(s_levelOne);
        float full = world.Stamina();

        world.SwingAtBoulder();
        world.Idle(1);

        world.Stamina().Should().Be(full - StaminaConfig.LoadShippedDefaults().ItemSwingCosts["wood_sword"], "a swing that connects pays the wood sword's cost");
    }

    [Fact]
    public void Attack_WhiffWhileExhausted_IsAllowedButAHitIsRefused()
    {
        StaminaPacingWorld world = StaminaPacingWorld.CreateAtBoulder(s_levelOne);
        _ = world.SwingUntilRefused();
        world.IsExhausted.Should().BeTrue();
        world.Idle(StaminaPacingWorld.TicksPerSecond / 2); // let the last cooldown end
        uint refusals = world.Refusals;
        float low = world.Stamina();

        world.Whiff();
        world.Idle(1);
        world.SwingAtBoulder();
        world.Idle(1);

        world.Refusals.Should().Be(refusals, "the whiff was allowed and started the cooldown, which holds the next swing");
        world.Stamina().Should().BeGreaterThanOrEqualTo(low, "a whiff costs nothing");

        world.Idle(StaminaPacingWorld.TicksPerSecond / 2);
        world.SwingAtBoulder();
        world.Idle(1);
        world.Refusals.Should().Be(refusals + 1U, "a swing that connects while exhausted is refused");
    }

    [Fact]
    public void StepFixedTick_AfterASwing_RegenWaitsForTheConfiguredDelayThenStarts()
    {
        StaminaPacingWorld world = StaminaPacingWorld.CreateAtBoulder(s_levelOne);
        world.SwingDownTo(world.MaxStamina());
        float afterSwing = world.Stamina();
        int delayTicks = (int)(world.World.StaminaConfig.RegenDelaySeconds * StaminaPacingWorld.TicksPerSecond);

        world.Idle(delayTicks - 2);
        world.Stamina().Should().Be(afterSwing, "regen waits for the delay after a spend");

        world.Idle(4);
        world.Stamina().Should().BeGreaterThan(afterSwing, "regen starts once the delay has passed");
    }
}
