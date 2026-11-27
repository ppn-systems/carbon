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
    /// Resolves how much of a hit lands, without changing any state: nothing on an invalid, dead, health-less or
    /// invulnerable target, otherwise <paramref name="rawDamage"/> after <paramref name="mitigation"/>. Every damage path
    /// (engine systems, player hits on monsters, monster hits on players) calls this (audit DOM-1).
    /// </summary>
    /// <typeparam name="TMitigation">Mitigation policy (a struct, so the call does not allocate).</typeparam>
    /// <param name="world">Owning world simulation.</param>
    /// <param name="target">Victim entity.</param>
    /// <param name="rawDamage">Pre-mitigation damage.</param>
    /// <param name="mitigation">Mitigation policy.</param>
    /// <param name="effectiveDamage">Damage that lands (0 when the hit is refused).</param>
    /// <returns><see langword="true"/> when the target can take the hit.</returns>
    public static bool TryResolve<TMitigation>(WorldSimulation world, Entity target, float rawDamage, in TMitigation mitigation, out float effectiveDamage)
        where TMitigation : struct, IDamageMitigation
    {
        ArgumentNullException.ThrowIfNull(world);
        effectiveDamage = 0f;
        EntityManager entities = world.Entities;
        if (!target.IsValid || !entities.IsAlive(target) || !entities.HasHealth(target))
        {
            return false;
        }

        if (entities.TryGetComponent(target, out CombatStateComponent combatState) && combatState.IsInvulnerable)
        {
            return false;
        }

        effectiveDamage = rawDamage > 0f ? MathF.Max(0f, mitigation.Mitigate(world, target, rawDamage)) : rawDamage;
        return true;
    }

    /// <summary>
    /// Applies <paramref name="rawDamage"/> from <paramref name="attacker"/> to <paramref name="target"/>, mitigated by
    /// the target's armor (<see cref="ArmorDamageMitigation"/>).
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
        if (!TryResolve(world, target, rawDamage, default(ArmorDamageMitigation), out float effectiveDamage))
        {
            return false;
        }

        ref HealthComponent health = ref world.Entities.GetHealth(target);
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
