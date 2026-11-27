// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Abstractions.World;
using Carbon.Application.World.StatusEffects;
using Carbon.Combat.Statuses;
using Carbon.Creatures;
using Carbon.Simulation.ECS;
using Carbon.Simulation.World;
using Carbon.Survival.Components;
using Carbon.Survival.Config;
using FluentAssertions;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>ADR-029 server rules: procs on the secret roll, heavy hits, the poison fold, derived effects and persistence.</summary>
public sealed class StatusEffectServiceTests
{
    private const ushort WildSlimeArchetype = 101;
    private const ushort NoProcArchetype = 9999;
    private const ushort Slowed = 2;
    private const ushort Stunned = 4;

    [Fact]
    public void OnMonsterHit_FixedEntropy_SlimeProcsSlowOnThePinnedTicks()
    {
        List<ulong> procTicks = ProcTicks(entropyLabel: 7UL);

        procTicks.Should().Equal(
            [1UL, 3UL, 5UL, 9UL, 11UL, 12UL, 14UL, 17UL, 18UL, 21UL, 22UL, 28UL, 33UL],
            "a fixed secret replays the same 35 % slime slows");
        ProcTicks(entropyLabel: 8UL).Should().NotEqual(procTicks, "the roll follows the secret");
    }

    [Fact]
    public void OnMonsterHit_HeavyHit_StunsAtAQuarterOfMaxHealthOnly()
    {
        Harness h = new(entropyLabel: 1UL);

        h.Service.OnMonsterHit(h.Player, 10UL, Entity.Null, 20UL, NoProcArchetype, dealt: 24.9f, victimMaxHealth: 100f, tick: 1UL).Should().BeFalse();
        h.Buffer.TryGet(Stunned, out _).Should().BeFalse();

        h.Service.OnMonsterHit(h.Player, 10UL, Entity.Null, 20UL, NoProcArchetype, dealt: 25f, victimMaxHealth: 100f, tick: 2UL).Should().BeTrue();
        h.Buffer.TryGet(Stunned, out ActiveStatusEffect stun).Should().BeTrue();
        stun.RemainingTicks.Should().Be(15U);
        stun.Source.Should().Be(StatusSource.HeavyHit);
        h.Service.IsStunned(h.Player).Should().BeTrue();
        h.Service.GetMoveScale(h.Player).Should().Be(0f);
    }

    [Fact]
    public void MirrorPoisonIntoNeeds_FoodPoisoning_DrivesTheNeedsPoisonUntilTheEffectEnds()
    {
        Harness h = new(entropyLabel: 1UL);
        h.Service.Apply(h.Player, "poisoned", StatusSource.Food, durationSeconds: 120f).Should().BeTrue();

        h.Service.MirrorPoisonIntoNeeds(h.Player);
        h.Simulation.Entities.GetComponent<NeedsComponent>(h.Player).PoisonRemainingSeconds.Should().Be(120f);

        h.Service.Clear(h.Player);
        h.Service.MirrorPoisonIntoNeeds(h.Player);
        h.Simulation.Entities.GetComponent<NeedsComponent>(h.Player).IsPoisoned.Should().BeFalse("the effect is the only source of poison");
    }

    [Fact]
    public void CollectEntries_HungryExhaustedAndWellFed_AreDerivedFromNeeds()
    {
        Harness h = new(entropyLabel: 1UL);
        ref NeedsComponent needs = ref h.Simulation.Entities.GetComponent<NeedsComponent>(h.Player);
        needs.Hunger = HungerState.UrgentlyHungry;
        needs.Fatigue = RestState.Exhausted;
        List<StatusEffectEntry> entries = [];

        h.Service.CollectEntries(h.Player, h.Needs, entries);

        entries.Should().Equal(
            new StatusEffectEntry(5, 2, 0U, 0U, 0.8f, (byte)StatusSource.Needs, 0),
            new StatusEffectEntry(7, 3, 0U, 0U, 0.85f, (byte)StatusSource.Needs, 0));

        needs.Hunger = HungerState.Fed;
        needs.Fatigue = RestState.Rested;
        entries.Clear();
        h.Service.CollectEntries(h.Player, h.Needs, entries);
        entries.Should().Equal(new StatusEffectEntry(8, 1, 0U, 0U, 1.5f, (byte)StatusSource.Needs, 0));
        h.Service.GetHealthRegenMultiplier(h.Player, h.Needs).Should().Be(1.5f);
    }

    [Fact]
    public void TryGrantRested_WakingAtNinetyFivePercentRest_GrantsTheStaminaBuffOnlyThen()
    {
        Harness h = new(entropyLabel: 1UL);
        ref NeedsComponent needs = ref h.Simulation.Entities.GetComponent<NeedsComponent>(h.Player);
        needs.Rest = 94f;
        h.Service.TryGrantRested(h.Player).Should().BeFalse();

        needs.Rest = 95f;
        h.Service.TryGrantRested(h.Player).Should().BeTrue();
        h.Service.GetStaminaRegenMultiplier(h.Player).Should().Be(1.25f);
        h.Buffer.TryGet(9, out ActiveStatusEffect rested).Should().BeTrue();
        rested.RemainingTicks.Should().Be(18_000U);
    }

    [Fact]
    public void Serialize_SavesOnlyPersistedEffects()
    {
        Harness h = new(entropyLabel: 1UL);
        _ = h.Service.Apply(h.Player, "poisoned", StatusSource.Monster);
        _ = h.Service.Apply(h.Player, "poisoned", StatusSource.Monster);
        _ = h.Service.Apply(h.Player, "slowed", StatusSource.Monster);

        h.Service.Serialize(h.Player).Should().Be("poisoned:10000:2", "a 3 s slow is not worth a relog");
    }

    [Fact]
    public void Restore_SkipsUnknownAndMalformedEntriesAndClampsDurations()
    {
        Harness h = new(entropyLabel: 1UL, spawnBuffer: false);

        h.Service.Restore(h.Player, "poisoned:4000:2;bogus:1000:1;bleeding:x:1;slowed:2000:1;rested:999999999:1", 0f, h.Needs);

        h.Buffer.TryGet(1, out ActiveStatusEffect poison).Should().BeTrue();
        (poison.RemainingTicks, poison.Stacks).Should().Be((120U, 2));
        h.Buffer.TryGet(9, out ActiveStatusEffect rested).Should().BeTrue();
        rested.RemainingTicks.Should().Be(18_000U, "rested is clamped to its 600 s duration");
        h.Buffer.Count.Should().Be(2, "unknown, malformed and never-saved effects are skipped");
    }

    [Fact]
    public void Restore_RowSavedBeforeStatusEffects_RestoresPoisonFromTheNeedsColumn()
    {
        Harness h = new(entropyLabel: 1UL, spawnBuffer: false);

        h.Service.Restore(h.Player, string.Empty, legacyPoisonSeconds: 30f, h.Needs);

        h.Buffer.TryGet(1, out ActiveStatusEffect poison).Should().BeTrue();
        poison.RemainingTicks.Should().Be(900U);
        poison.Source.Should().Be(StatusSource.Food);
    }

    private static List<ulong> ProcTicks(ulong entropyLabel)
    {
        Harness h = new(entropyLabel);
        List<ulong> ticks = [];
        for (ulong tick = 1UL; tick <= 40UL; tick++)
        {
            h.Service.Clear(h.Player);
            if (h.Service.OnMonsterHit(h.Player, 10UL, Entity.Null, 20UL, WildSlimeArchetype, dealt: 5f, victimMaxHealth: 100f, tick))
            {
                h.Buffer.TryGet(Slowed, out ActiveStatusEffect slow).Should().BeTrue();
                slow.SourceId.Should().Be(WildSlimeArchetype);
                ticks.Add(tick);
            }
        }

        return ticks;
    }

    private sealed class Harness
    {
        public Harness(ulong entropyLabel, bool spawnBuffer = true)
        {
            this.Service = new StatusEffectService(this.Simulation, 30, null, CreatureContent.LoadDefault().Catalog, TestGameplayEntropy.Create(entropyLabel));
            this.Player = this.Simulation.SpawnEntity(Vector2.Zero, radius: 8f);
            this.Simulation.Entities.SetComponent(this.Player, NeedsComponent.CreateFull(this.Needs));
            if (spawnBuffer)
            {
                _ = this.Service.Attach(this.Player);
            }
        }

        public WorldSimulation Simulation { get; } = new(tickRate: 30);

        public NeedsConfig Needs { get; } = new();

        public StatusEffectService Service { get; }

        public Entity Player { get; }

        public StatusEffectBuffer Buffer => this.Service.TryGetBuffer(this.Player, out StatusEffectBuffer buffer) ? buffer : throw new InvalidOperationException("no buffer");
    }
}
