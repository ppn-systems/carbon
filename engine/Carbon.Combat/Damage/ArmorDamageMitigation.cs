// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Combat.Stats;
using Carbon.Simulation.ECS;
using Carbon.Simulation.World;

namespace Carbon.Combat.Damage;

/// <summary>
/// Mitigation for creatures: the target's <see cref="StatSheet"/> <see cref="StatType.Defense"/> (scaled monster armor
/// from <c>spawning.json</c>) reduces damage by <c>100 / (100 + defense)</c>. Players use the
/// <c>progression.json</c> rule instead (ADR-021).
/// </summary>
public readonly struct ArmorDamageMitigation : IDamageMitigation
{
    /// <inheritdoc/>
    public float Mitigate(WorldSimulation world, Entity target, float rawDamage)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.Entities.GetAttachmentStore<StatSheet>().TryGet(target, out StatSheet? stats)
            ? rawDamage * (100f / (100f + MathF.Max(0f, stats.GetValue(StatType.Defense))))
            : rawDamage;
    }
}
