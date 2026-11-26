// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Creatures.Definitions;

/// <summary>
/// Level-1 daytime base stats of a monster before <see cref="Scaling.MonsterScaling"/> curves are applied.
/// </summary>
public sealed class MonsterBaseStats
{
    /// <summary>Gets the base maximum health.</summary>
    public float Hp { get; set; }

    /// <summary>Gets the base damage per hit (multiplied by each attack's damage multiplier).</summary>
    public float Damage { get; set; }

    /// <summary>Gets the base armor, applied as <see cref="Combat.Stats.StatType.Defense"/> mitigation.</summary>
    public float Armor { get; set; }

    /// <summary>Gets the base movement speed in world units per second.</summary>
    public float Speed { get; set; }
}
