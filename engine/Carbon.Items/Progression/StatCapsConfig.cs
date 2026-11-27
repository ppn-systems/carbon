// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Items.Progression;

/// <summary>Hard caps applied after base, level, attribute and gear contributions are summed.</summary>
public sealed class StatCapsConfig
{
    /// <summary>Gets the total critical chance cap (weapon included).</summary>
    public float CritChance { get; init; } = 0.5f;

    /// <summary>Gets the critical damage multiplier cap.</summary>
    public float CritDamage { get; init; } = 2.5f;

    /// <summary>Gets the attack speed multiplier cap.</summary>
    public float AttackSpeed { get; init; } = 1.5f;

    /// <summary>Gets the movement speed multiplier cap.</summary>
    public float MoveSpeed { get; init; } = 1.25f;
}
