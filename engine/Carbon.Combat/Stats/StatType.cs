// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Combat.Stats;

/// <summary>
/// Core numeric attribute identifiers managed by <see cref="StatSheet"/>.
/// </summary>
public enum StatType : byte
{
    /// <summary>Maximum health points.</summary>
    MaxHealth = 0,

    /// <summary>Base movement speed in world units per second.</summary>
    MoveSpeed = 1,

    /// <summary>Base offensive attack power.</summary>
    AttackPower = 2,

    /// <summary>Defensive damage reduction rating.</summary>
    Defense = 3,

    /// <summary>Attack cadence multiplier.</summary>
    AttackSpeed = 4,

    /// <summary>Critical strike probability [0.0 - 1.0].</summary>
    CritChance = 5,

    /// <summary>Critical strike damage multiplier (e.g. 1.5x or 2.0x).</summary>
    CritMultiplier = 6
}
