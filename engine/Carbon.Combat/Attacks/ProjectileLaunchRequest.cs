// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Simulation.ECS;

namespace Carbon.Combat.Attacks;

/// <summary>
/// Parameters of a projectile released by a <see cref="AttackKind.Projectile"/> attack.
/// </summary>
/// <param name="Owner">Attacking entity.</param>
/// <param name="Origin">Spawn position.</param>
/// <param name="Velocity">Linear velocity in world units per second.</param>
/// <param name="Damage">Damage on impact.</param>
/// <param name="TargetLayerMask">Collider layer mask of valid victims.</param>
/// <param name="LifetimeTicks">Lifetime before automatic despawn.</param>
/// <param name="Radius">Collision radius.</param>
/// <param name="VisualArchetype">Client projectile visual id from <see cref="AttackProfile.ProjectileVisual"/>.</param>
public readonly record struct ProjectileLaunchRequest(
    Entity Owner,
    Vector2 Origin,
    Vector2 Velocity,
    float Damage,
    uint TargetLayerMask,
    uint LifetimeTicks,
    float Radius,
    byte VisualArchetype);
