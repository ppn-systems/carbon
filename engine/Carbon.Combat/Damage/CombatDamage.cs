// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Combat.Components;
using Carbon.Combat.Events;
using Carbon.Combat.Stats;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.Networking;
using Carbon.Simulation.World;

namespace Carbon.Combat.Damage;

/// <summary>
/// Server-authoritative damage application shared by projectiles and attacks: honours invulnerability and
/// <see cref="StatType.Defense"/> mitigation, writes <see cref="HealthComponent"/>, and publishes
/// <see cref="DamageEvent"/> / <see cref="EntityKilledEvent"/>. Zero-allocation.
/// </summary>
public static class CombatDamage
{
    /// <summary>
    /// Applies <paramref name="rawDamage"/> from <paramref name="attacker"/> to <paramref name="target"/>.
    /// </summary>
    /// <param name="world">Owning world simulation.</param>
    /// <param name="attacker">Attacking entity credited with the hit.</param>
    /// <param name="target">Victim entity.</param>
    /// <param name="rawDamage">Pre-mitigation damage.</param>
    /// <param name="isCritical">Whether the hit is flagged critical.</param>
    /// <returns><see langword="true"/> if damage was applied; <see langword="false"/> if the target has no health or is invulnerable.</returns>
    public static bool Apply(WorldSimulation world, Entity attacker, Entity target, float rawDamage, bool isCritical = false)
    {
        ArgumentNullException.ThrowIfNull(world);

        EntityManager entities = world.Entities;
        if (!target.IsValid || !entities.IsAlive(target) || !entities.HasHealth(target))
        {
            return false;
        }

        if (entities.TryGetComponent(target, out CombatStateComponent combatState) && combatState.IsInvulnerable)
        {
            return false;
        }

        float effectiveDamage = rawDamage;
        EntityAttachmentStore<StatSheet> statsStore = entities.GetAttachmentStore<StatSheet>();
        if (effectiveDamage > 0f && statsStore.TryGet(target, out StatSheet? stats))
        {
            float defense = MathF.Max(0f, stats.GetValue(StatType.Defense));
            effectiveDamage *= 100f / (100f + defense);
        }

        ref HealthComponent health = ref entities.GetHealth(target);
        bool wasDead = health.IsDead;
        float nextHp = MathF.Max(0f, health.Current - effectiveDamage);
        health = new HealthComponent(nextHp, health.Max);
        world.MarkDirty(target, EntityDirtyMask.Health);

        world.PublishEvent(new DamageEvent(attacker, target, effectiveDamage, isCritical));
        if (!wasDead && health.IsDead)
        {
            world.PublishEvent(new EntityKilledEvent(attacker, target));
        }

        return true;
    }
}
