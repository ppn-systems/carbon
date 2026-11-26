// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Creatures.Scaling;

/// <summary>
/// Per-level stat multiplier curve: <c>min(MaxMultiplier, (1 + PerLevel * (level - 1)) ^ Exponent)</c>.
/// </summary>
public sealed class StatCurve
{
    /// <summary>Gets the linear growth per level.</summary>
    public float PerLevel { get; set; }

    /// <summary>Gets the curve exponent (1 = linear, &gt; 1 = accelerating).</summary>
    public float Exponent { get; set; } = 1f;

    /// <summary>Gets the multiplier cap.</summary>
    public float MaxMultiplier { get; set; } = 1000f;

    /// <summary>
    /// Evaluates the multiplier at <paramref name="level"/>.
    /// </summary>
    /// <param name="level">Monster level (1-based).</param>
    /// <returns>Stat multiplier (&gt;= 1 for non-negative growth).</returns>
    public float Evaluate(int level)
    {
        float linear = 1f + (this.PerLevel * Math.Max(0, level - 1));
        return MathF.Min(this.MaxMultiplier, MathF.Pow(MathF.Max(0f, linear), this.Exponent));
    }
}
