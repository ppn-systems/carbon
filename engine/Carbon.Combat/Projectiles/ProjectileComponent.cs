// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Simulation.ECS;

namespace Carbon.Combat.Projectiles;

/// <summary>
/// Unmanaged ECS component tagging an entity as an active ballistic projectile.
/// </summary>
/// <param name="Owner">Entity that fired the projectile (ignored during hit tests).</param>
/// <param name="Damage">Damage dealt on impact.</param>
/// <param name="TargetLayerMask">Collider layer mask of valid targets.</param>
/// <param name="RemainingTicks">Lifetime remaining in simulation ticks before automatic despawn.</param>
/// <param name="IsCritical">Whether impact damage should be flagged as critical.</param>
public readonly record struct ProjectileComponent(
    Entity Owner,
    float Damage,
    uint TargetLayerMask,
    uint RemainingTicks,
    bool IsCritical = false);
