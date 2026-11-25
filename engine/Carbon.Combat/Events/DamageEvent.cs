// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Simulation.ECS;

namespace Carbon.Combat.Events;

/// <summary>
/// Emitted when an entity takes damage from a combat action or projectile.
/// </summary>
/// <param name="Attacker">Attacking entity.</param>
/// <param name="Target">Damaged target entity.</param>
/// <param name="Amount">Damage amount applied.</param>
/// <param name="IsCritical">Whether the hit was a critical strike.</param>
public readonly record struct DamageEvent(
    Entity Attacker,
    Entity Target,
    float Amount,
    bool IsCritical);
