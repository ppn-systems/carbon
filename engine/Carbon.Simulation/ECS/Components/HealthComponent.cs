// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Simulation.ECS.Components;

/// <summary>
/// Represents standard health points (HP) for players, NPCs, and destructible entities.
/// Unmanaged struct component.
/// </summary>
/// <param name="Current">Current health value.</param>
/// <param name="Max">Maximum health capacity.</param>
public readonly record struct HealthComponent(float Current, float Max)
{
    /// <summary>
    /// Gets a value indicating whether the entity is dead (Current HP &lt;= 0).
    /// </summary>
    public bool IsDead => this.Current <= 0f;

    /// <summary>
    /// Gets the normalized health percentage [0.0 - 1.0].
    /// </summary>
    public float Fraction => this.Max > 0f ? MathF.Min(1f, MathF.Max(0f, this.Current / this.Max)) : 0f;
}
