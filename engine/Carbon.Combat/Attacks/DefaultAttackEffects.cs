// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Combat.Damage;
using Carbon.Combat.Projectiles;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.Networking;
using Carbon.Simulation.World;

namespace Carbon.Combat.Attacks;

/// <summary>
/// Engine-only <see cref="IAttackEffects"/>: hits go through <see cref="CombatDamage.Apply"/>, projectiles are plain
/// simulation entities resolved by <see cref="ProjectileSystem"/>.
/// </summary>
public sealed class DefaultAttackEffects : IAttackEffects
{
    /// <summary>Gets the shared instance.</summary>
    public static DefaultAttackEffects Instance { get; } = new();

    /// <inheritdoc/>
    public bool ApplyHit(WorldSimulation world, Entity attacker, Entity victim, float damage) =>
        CombatDamage.Apply(world, attacker, victim, damage);

    /// <inheritdoc/>
    public Entity LaunchProjectile(WorldSimulation world, in ProjectileLaunchRequest request)
    {
        ArgumentNullException.ThrowIfNull(world);

        EntityManager entities = world.Entities;
        Entity projectile = world.SpawnEntity(request.Origin, request.Radius);
        entities.SetCollider(projectile, Collider2D.CreateCircle(request.Radius, isTrigger: true));
        entities.SetVelocity(projectile, new Velocity2D(request.Velocity));
        entities.SetComponent(projectile, new ProjectileComponent(request.Owner, request.Damage, request.TargetLayerMask, request.LifetimeTicks));
        world.MarkDirty(projectile, EntityDirtyMask.Velocity);
        return projectile;
    }
}
