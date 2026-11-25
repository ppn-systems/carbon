// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Combat.Cooldowns;
using Carbon.Combat.Events;
using Carbon.Combat.Hitboxes;
using Carbon.Combat.Projectiles;
using Carbon.Combat.Stats;
using Carbon.Combat.Statuses;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.Networking;
using Carbon.Simulation.World;
using FluentAssertions;
using Xunit;

namespace Carbon.Combat.Tests;

public sealed class CombatModuleTests
{
    [Fact]
    public void StatSheet_AddAndRemoveModifiers_RecomputesExactValuesWithoutDrift()
    {
        StatSheet sheet = new();
        sheet.SetBase(StatType.AttackPower, 100f);

        sheet.AddModifier(new StatModifier(SourceId: 1, StatType.AttackPower, StatModKind.Flat, 20f));
        sheet.AddModifier(new StatModifier(SourceId: 2, StatType.AttackPower, StatModKind.PercentAdd, 0.25f));
        sheet.AddModifier(new StatModifier(SourceId: 3, StatType.AttackPower, StatModKind.PercentMult, 0.10f));

        // (100 + 20) * (1 + 0.25) * 1.10 = 165
        sheet.GetValue(StatType.AttackPower).Should().BeApproximately(165f, 0.001f);

        sheet.RemoveModifiersFromSource(2).Should().Be(1);
        // (100 + 20) * 1.0 * 1.10 = 132
        sheet.GetValue(StatType.AttackPower).Should().BeApproximately(132f, 0.001f);
    }

    [Fact]
    public void CooldownTracker_StartAndQuery_TracksTickReadiness()
    {
        CooldownTracker tracker = new();
        tracker.IsReady(abilityId: 7, currentTick: 10).Should().BeTrue();

        tracker.StartCooldown(abilityId: 7, currentTick: 10, durationTicks: 20);
        tracker.IsReady(7, 29).Should().BeFalse();
        tracker.GetRemainingTicks(7, 25).Should().Be(5);
        tracker.IsReady(7, 30).Should().BeTrue();
    }

    [Fact]
    public void StatusEffectBuffer_StacksPulsesAndExpiresCleanly()
    {
        StatusEffectBuffer buffer = new();
        Entity caster = new(1, 1);
        Entity target = new(2, 1);

        ActiveStatusEffect poison = new(
            EffectId: 40,
            Caster: caster,
            ControlMask: CrowdControlMask.Slowed,
            MagnitudePerStack: 5f,
            RemainingTicks: 4,
            PulseIntervalTicks: 2,
            TicksUntilNextPulse: 2,
            Stacks: 1,
            MaxStacks: 3,
            IsDamage: true);

        buffer.ApplyOrRefresh(in poison);
        buffer.ApplyOrRefresh(in poison); // Stacks -> 2
        buffer.ActiveControlMask.Should().Be(CrowdControlMask.Slowed);

        Span<StatusPulseEvent> pulses = stackalloc StatusPulseEvent[4];
        buffer.AdvanceTick(target, pulses, out int pulseCount1);
        pulseCount1.Should().Be(0);

        buffer.AdvanceTick(target, pulses, out int pulseCount2);
        pulseCount2.Should().Be(1);
        pulses[0].TotalMagnitude.Should().BeApproximately(10f, 0.001f);

        buffer.AdvanceTick(target, pulses, out _);
        buffer.AdvanceTick(target, pulses, out int pulseCount4);
        pulseCount4.Should().Be(1);
        buffer.Count.Should().Be(0);
        buffer.ActiveControlMask.Should().Be(CrowdControlMask.None);
    }

    [Fact]
    public void ProjectileSystem_HitsTarget_PublishesDamageAndKillEventsAndDespawns()
    {
        WorldSimulation world = new(tickRate: 20);
        world.RegisterSystem(new ProjectileSystem());

        Entity attacker = world.SpawnEntity(new Vector2(0f, 0f), radius: 5f);
        Entity enemy = world.SpawnEntity(new Vector2(40f, 0f), radius: 10f);
        world.Entities.SetHealth(enemy, new HealthComponent(25f, 25f));

        Entity bolt = world.SpawnEntity(new Vector2(10f, 0f), radius: 2f);
        world.Entities.SetCollider(bolt, Collider2D.CreateCircle(2f, isTrigger: true));
        world.Entities.SetVelocity(bolt, new Velocity2D(600f, 0f)); // Moves 30u in 1 tick (10 -> 40)
        world.Entities.SetComponent(bolt, new ProjectileComponent(attacker, Damage: 30f, TargetLayerMask: uint.MaxValue, RemainingTicks: 20, IsCritical: true));

        _ = world.Update(0.05);

        world.Entities.IsAlive(bolt).Should().BeFalse();
        world.Entities.GetHealth(enemy).IsDead.Should().BeTrue();
        world.GetDirtyFlags(enemy).HasFlag(EntityDirtyMask.Health).Should().BeTrue();

        ReadOnlySpan<DamageEvent> damages = world.ReadEvents<DamageEvent>();
        damages.Length.Should().Be(1);
        damages[0].Amount.Should().Be(30f);
        damages[0].IsCritical.Should().BeTrue();

        ReadOnlySpan<EntityKilledEvent> kills = world.ReadEvents<EntityKilledEvent>();
        kills.Length.Should().Be(1);
        kills[0].Victim.Should().Be(enemy);
    }

    [Fact]
    public void HitboxQuery_QueryArc_FiltersByConeAngle()
    {
        WorldSimulation world = new(tickRate: 20);
        Entity attacker = world.SpawnEntity(new Vector2(0f, 0f), radius: 5f);
        Entity inFront = world.SpawnEntity(new Vector2(20f, 0f), radius: 5f);
        _ = world.SpawnEntity(new Vector2(-20f, 0f), radius: 5f); // Behind

        Span<Entity> hits = stackalloc Entity[8];
        HitboxQuery.QueryArc(world, Vector2.Zero, Vector2.UnitX, radius: 35f, halfAngleRadians: 0.785f, hits, out int hitCount, ignoreEntity: attacker);

        hitCount.Should().Be(1);
        hits[0].Should().Be(inFront);
    }
}
