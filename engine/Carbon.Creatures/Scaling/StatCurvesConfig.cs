// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Creatures.Scaling;

/// <summary>
/// Per-stat level scaling curves.
/// </summary>
public sealed class StatCurvesConfig
{
    /// <summary>Gets the health curve.</summary>
    public StatCurve Hp { get; set; } = new();

    /// <summary>Gets the damage curve.</summary>
    public StatCurve Damage { get; set; } = new();

    /// <summary>Gets the armor curve.</summary>
    public StatCurve Armor { get; set; } = new();

    /// <summary>Gets the movement speed curve.</summary>
    public StatCurve Speed { get; set; } = new();
}
