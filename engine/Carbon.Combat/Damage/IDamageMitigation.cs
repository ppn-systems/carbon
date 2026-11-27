// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Simulation.ECS;
using Carbon.Simulation.World;

namespace Carbon.Combat.Damage;

/// <summary>
/// Turns raw damage into the damage a target takes (audit DOM-1). Every hit goes through
/// <see cref="CombatDamage.TryResolve{TMitigation}"/> with one of these, so the invulnerability check and the
/// mitigation rule exist once. Implement it on a struct so resolving a hit never allocates.
/// </summary>
public interface IDamageMitigation
{
    /// <summary>Returns the damage <paramref name="target"/> takes from a hit of <paramref name="rawDamage"/> (&gt; 0).</summary>
    /// <param name="world">Owning world simulation.</param>
    /// <param name="target">Victim entity.</param>
    /// <param name="rawDamage">Pre-mitigation damage.</param>
    /// <returns>Damage after mitigation.</returns>
    float Mitigate(WorldSimulation world, Entity target, float rawDamage);
}
