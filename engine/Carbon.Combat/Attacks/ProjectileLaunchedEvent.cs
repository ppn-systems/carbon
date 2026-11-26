// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Simulation.ECS;

namespace Carbon.Combat.Attacks;

/// <summary>
/// Published by <see cref="AttackSystem"/> when a <see cref="AttackKind.Projectile"/> attack releases its projectile.
/// The projectile itself is a regular simulated entity resolved by <see cref="Projectiles.ProjectileSystem"/>;
/// <see cref="ArcHeight"/> and <see cref="FlightTicks"/> let clients draw a visual arc.
/// </summary>
/// <param name="Owner">Attacking entity.</param>
/// <param name="Projectile">Spawned projectile entity.</param>
/// <param name="Origin">Spawn position.</param>
/// <param name="Velocity">Linear velocity in world units per second.</param>
/// <param name="ArcHeight">Visual apex height.</param>
/// <param name="FlightTicks">Expected ticks to reach the aim point.</param>
public readonly record struct ProjectileLaunchedEvent(
    Entity Owner,
    Entity Projectile,
    Vector2 Origin,
    Vector2 Velocity,
    float ArcHeight,
    uint FlightTicks);
