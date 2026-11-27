// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Numerics;
using Carbon.Combat.Components;
using Carbon.Combat.Events;
using Carbon.Combat.Statuses;
using Carbon.Combat.Statuses.Config;
using Carbon.Simulation.Clock;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.World;
using FluentAssertions;
using Xunit;

namespace Carbon.Combat.Tests;

/// <summary>ADR-029: stacking rules, damage pulses on exact ticks, speed and expiry, and the shipped definitions.</summary>
public sealed class StatusEffectTests
{
    private const ushort TickRate = 30;
    private static readonly StatusEffectCatalog s_shipped = StatusEffectCatalog.LoadShippedDefaults();

    [Fact]
    public void ApplyOrRefresh_RefreshRule_ResetsTheDurationAndKeepsOneStack()
    {
        StatusEffectBuffer buffer = new();
        ActiveStatusEffect stun = Create("stunned");
        buffer.ApplyOrRefresh(in stun).Should().BeTrue();
        Advance(buffer, 10);
        buffer.TryGet(stun.EffectId, out ActiveStatusEffect running).Should().BeTrue();
        running.RemainingTicks.Should().Be(5U, "0.5 s at 30 Hz is 15 ticks, 10 have run");

        buffer.ApplyOrRefresh(in stun).Should().BeTrue();

        buffer.TryGet(stun.EffectId, out ActiveStatusEffect refreshed).Should().BeTrue();
        refreshed.RemainingTicks.Should().Be(15U, "a refresh restarts the full duration");
        refreshed.Stacks.Should().Be(1);
    }

    [Fact]
    public void ApplyOrRefresh_StackRule_AddsStacksUpToTheCap()
    {
        StatusEffectBuffer buffer = new();
        ActiveStatusEffect poison = Create("poisoned");

        for (int i = 0; i < 4; i++)
        {
            _ = buffer.ApplyOrRefresh(in poison);
        }

        buffer.Count.Should().Be(1);
        buffer.TryGet(poison.EffectId, out ActiveStatusEffect stacked).Should().BeTrue();
        stacked.Stacks.Should().Be(3, "poisoned stacks up to 3");
        stacked.RemainingTicks.Should().Be(300U);
    }

    [Fact]
    public void ApplyOrRefresh_StrongestRule_IgnoresWeakerReplacesStrongerAndExtendsEqual()
    {
        StatusEffectBuffer buffer = new();
        ActiveStatusEffect slow = Create("slowed");
        _ = buffer.ApplyOrRefresh(in slow);
        Advance(buffer, 30);

        buffer.ApplyOrRefresh(slow with { MoveSpeedMultiplier = 0.8f }).Should().BeFalse("a weaker slow is ignored");
        buffer.MoveSpeedScale.Should().BeApproximately(0.6f, 1e-6f);
        buffer.TryGet(slow.EffectId, out ActiveStatusEffect afterWeak).Should().BeTrue();
        afterWeak.RemainingTicks.Should().Be(60U);

        buffer.ApplyOrRefresh(in slow).Should().BeTrue("an equal slow extends the duration");
        buffer.TryGet(slow.EffectId, out ActiveStatusEffect afterEqual).Should().BeTrue();
        afterEqual.RemainingTicks.Should().Be(90U);

        buffer.ApplyOrRefresh(slow with { MoveSpeedMultiplier = 0.4f, RemainingTicks = 30U, DurationTicks = 30U }).Should().BeTrue();
        buffer.MoveSpeedScale.Should().BeApproximately(0.4f, 1e-6f, "a stronger slow replaces the active one");
        buffer.TryGet(slow.EffectId, out ActiveStatusEffect afterStrong).Should().BeTrue();
        afterStrong.RemainingTicks.Should().Be(30U);
    }

    [Fact]
    public void Version_ChangesOnApplyAndExpiry_ButNotOnPulsesOrCountdown()
    {
        StatusEffectBuffer buffer = new();
        uint start = buffer.Version;
        _ = buffer.ApplyOrRefresh(Create("poisoned"));
        uint applied = buffer.Version;
        applied.Should().NotBe(start);

        Advance(buffer, 299);
        buffer.Version.Should().Be(applied, "four pulses and the countdown are not news to the client");

        Advance(buffer, 1);
        buffer.Count.Should().Be(0);
        buffer.Version.Should().NotBe(applied, "the expiry must be sent");
    }

    [Fact]
    public void StatusEffectSystem_Poisoned_DealsOnePerStackEveryTwoSecondsOnExactTicksThenExpires()
    {
        (WorldSimulation world, Entity target, StatusEffectBuffer buffer) = CreateWorld(health: 100f);
        _ = buffer.ApplyOrRefresh(Create("poisoned"));
        _ = buffer.ApplyOrRefresh(Create("poisoned"));

        Step(world, 59);
        world.Entities.GetHealth(target).Current.Should().Be(100f, "the first pulse is due 2 s (60 ticks) after the apply");

        Step(world, 1);
        world.Entities.GetHealth(target).Current.Should().Be(98f, "two stacks deal 2 per pulse");

        Step(world, 240);
        world.Entities.GetHealth(target).Current.Should().Be(90f, "pulses land on ticks 60, 120, 180, 240 and 300");
        buffer.Count.Should().Be(0, "10 s have elapsed");

        Step(world, 60);
        world.Entities.GetHealth(target).Current.Should().Be(90f, "an expired effect deals nothing");
    }

    [Fact]
    public void StatusEffectSystem_NonLethalPoison_StopsAtOneHitPoint()
    {
        (WorldSimulation world, Entity target, StatusEffectBuffer buffer) = CreateWorld(health: 1.5f);
        _ = buffer.ApplyOrRefresh(Create("poisoned"));

        Step(world, 60);
        world.Entities.GetHealth(target).Current.Should().Be(1f);

        Step(world, 60);
        world.Entities.GetHealth(target).Current.Should().Be(1f, "poison never takes the last hit point");
    }

    [Fact]
    public void StatusEffectSystem_LethalBleeding_KillsAndPublishesTheKill()
    {
        (WorldSimulation world, Entity target, StatusEffectBuffer buffer) = CreateWorld(health: 0.5f);
        _ = buffer.ApplyOrRefresh(Create("bleeding"));

        Step(world, 30);

        world.Entities.GetHealth(target).Current.Should().Be(0f);
        world.ReadEvents<EntityKilledEvent>().Length.Should().Be(1);
        world.ReadEvents<StatusPulseEvent>()[0].Applied.Should().Be(0.5f, "the pulse reports what it actually removed");
    }

    [Fact]
    public void StatusEffectSystem_SlowAndStun_SetTheMoveScaleAndExpiryRestoresIt()
    {
        (WorldSimulation world, Entity target, StatusEffectBuffer buffer) = CreateWorld(health: 100f);
        _ = buffer.ApplyOrRefresh(Create("slowed"));

        Step(world, 1);
        world.Entities.GetComponent<CombatStateComponent>(target).MoveSpeedScale.Should().BeApproximately(0.6f, 1e-6f);

        _ = buffer.ApplyOrRefresh(Create("stunned"));
        Step(world, 1);
        world.Entities.GetComponent<CombatStateComponent>(target).MoveSpeedScale.Should().Be(0f);
        world.Entities.GetComponent<CombatStateComponent>(target).CanAct.Should().BeFalse();

        Step(world, 88);
        buffer.Count.Should().Be(0, "the 3 s slow ends on tick 90");
        world.Entities.GetComponent<CombatStateComponent>(target).MoveSpeedScale.Should().Be(1f);
    }

    [Fact]
    public void LoadFromJson_InvalidDocuments_AreRejected()
    {
        const string Effect = """{ "id": 1, "key": "a", "icon": "i", "durationSeconds": 1 }""";
        string[] invalid =
        [
            $$"""{ "schemaVersion": 1, "effects": [ {{Effect}}, {{Effect.Replace("\"a\"", "\"b\"", StringComparison.Ordinal)}} ] }""",
            $$"""{ "schemaVersion": 1, "effects": [ {{Effect}} ], "monsterProcs": [ { "monster": "m", "effect": "missing", "chance": 0.5 } ] }""",
            """{ "schemaVersion": 1, "effects": [ { "id": 1, "key": "a", "icon": "i", "derived": "Hungry", "durationSeconds": 5 } ] }""",
            """{ "schemaVersion": 1, "effects": [ { "id": 1, "key": "a", "icon": "i", "durationSeconds": 1, "magnitude": 2 } ] }""",
            """{ "schemaVersion": 1, "effects": [ { "id": 1, "key": "A b", "icon": "i", "durationSeconds": 1 } ] }""",
            """{ "schemaVersion": 1, "effects": [], "unknown": 1 }""",
        ];

        foreach (string json in invalid)
        {
            Action load = () => StatusEffectCatalog.LoadFromJson(json);
            load.Should().Throw<InvalidDataException>(json);
        }
    }

    [Fact]
    public void LoadShippedDefaults_HasTheAdrEffectsAndRules()
    {
        s_shipped.Effects.Should().HaveCount(10);
        s_shipped.HeavyHitEffect!.Key.Should().Be("stunned");
        s_shipped.HeavyHitMinDamageFraction.Should().Be(0.25f);
        s_shipped.RestedEffect!.Key.Should().Be("rested");
        s_shipped.GetDerived(StatusDerivation.Tired)!.MaxStacks.Should().Be(3);
        s_shipped.TryGetByKey("well_fed", out StatusEffectDefinition wellFed).Should().BeTrue();
        wellFed.HealthRegenMultiplier.Should().Be(1.5f);

        // ADR-032: drowning kills, and nothing heals a player while it lasts.
        s_shipped.TryGetByKey("drowning", out StatusEffectDefinition drowning).Should().BeTrue();
        drowning.Lethal.Should().BeTrue();
        drowning.HealthRegenMultiplier.Should().Be(0f);
    }

    private static ActiveStatusEffect Create(string key)
    {
        s_shipped.TryGetByKey(key, out StatusEffectDefinition def).Should().BeTrue();
        return StatusEffectCatalog.CreateActive(def, TickRate, Entity.Null, StatusSource.Monster, 101);
    }

    private static void Advance(StatusEffectBuffer buffer, int ticks)
    {
        Span<StatusPulseEvent> pulses = stackalloc StatusPulseEvent[8];
        for (int i = 0; i < ticks; i++)
        {
            buffer.AdvanceTick(Entity.Null, pulses, out _);
        }
    }

    private static (WorldSimulation World, Entity Target, StatusEffectBuffer Buffer) CreateWorld(float health)
    {
        WorldSimulation world = new(tickRate: TickRate);
        world.RegisterSystem(new StatusEffectSystem());
        Entity target = world.SpawnEntity(new Vector2(20f, 0f), radius: 4f);
        world.Entities.SetHealth(target, new HealthComponent(health, 100f));
        StatusEffectBuffer buffer = world.Entities.GetAttachmentStore<StatusEffectBuffer>().GetOrAdd(target, static () => new StatusEffectBuffer());
        return (world, target, buffer);
    }

    private static void Step(WorldSimulation world, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            ulong tick = (ulong)i + 1UL; // StatusEffectSystem counts its own ticks; the index is informational.
            world.ClearEvents<StatusPulseEvent>();
            world.ClearEvents<EntityKilledEvent>();
            world.ClearEvents<DamageEvent>();
            world.Tick(new FixedTickArgs(tick, 1f / TickRate, tick / (double)TickRate));
        }
    }
}
