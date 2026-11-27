// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Combat.Damage;
using Carbon.Items.Catalog;
using Carbon.Items.Progression;
using Carbon.Simulation.ECS;
using Carbon.Simulation.World;

namespace Carbon.Application.World.Progression;

/// <summary>
/// Player mitigation (ADR-021 §4.2): defense or magic resist from <c>progression.json</c>, with diminishing returns
/// against the attacker's level. Plugged into <see cref="CombatDamage.TryResolve{TMitigation}"/> (audit DOM-1).
/// </summary>
/// <param name="Config">Progression tuning.</param>
/// <param name="Stats">Defender's derived stats.</param>
/// <param name="Kind">Damage kind.</param>
/// <param name="AttackerLevel">Attacker level.</param>
internal readonly record struct ProgressionDamageMitigation(ProgressionConfig Config, DerivedCharacterStats Stats, DamageKind Kind, int AttackerLevel)
    : IDamageMitigation
{
    /// <inheritdoc/>
    public float Mitigate(WorldSimulation world, Entity target, float rawDamage)
    {
        DerivedCharacterStats stats = this.Stats;
        return this.Config.Mitigate(rawDamage, this.Kind, in stats, this.AttackerLevel);
    }
}
