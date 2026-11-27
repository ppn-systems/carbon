// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;

namespace Carbon.Items.Progression;

/// <summary>
/// Diminishing-returns curve that turns a rating (defense, magic resist, evasion) into a fraction:
/// <c>min(Cap, rating / (rating + K + PerAttackerLevel * attackerLevel))</c>. The attacker's level keeps a fixed rating
/// from trivialising higher-level content, and the hyperbola never reaches 100%.
/// </summary>
public sealed class MitigationCurve
{
    /// <summary>Gets the rating at which a level-0 attacker is mitigated by 50% (before the cap).</summary>
    public float K { get; init; } = 50f;

    /// <summary>Gets the extra rating needed for 50% per attacker level.</summary>
    public float PerAttackerLevel { get; init; } = 5f;

    /// <summary>Gets the hard cap on the fraction (0..0.95).</summary>
    public float Cap { get; init; } = 0.75f;

    /// <summary>Evaluates the fraction for <paramref name="rating"/> against an attacker of <paramref name="attackerLevel"/>.</summary>
    /// <param name="rating">Defender rating (negative is treated as 0).</param>
    /// <param name="attackerLevel">Attacker level (below 1 is treated as 1).</param>
    /// <returns>A fraction in <c>[0, Cap]</c>.</returns>
    public float Evaluate(float rating, int attackerLevel)
    {
        float r = float.IsFinite(rating) ? MathF.Max(0f, rating) : 0f;
        if (r <= 0f)
        {
            return 0f;
        }

        float denominator = r + this.K + (this.PerAttackerLevel * Math.Max(1, attackerLevel));
        return MathF.Min(this.Cap, r / denominator);
    }

    /// <summary>Checks the curve's ranges.</summary>
    /// <param name="name">Section name for error messages.</param>
    /// <exception cref="InvalidDataException">Thrown when a value is out of range.</exception>
    public void Validate(string name)
    {
        if (!(this.K > 0f && this.K <= 100_000f) ||
            !(this.PerAttackerLevel >= 0f && this.PerAttackerLevel <= 10_000f) ||
            !(this.Cap > 0f && this.Cap <= 0.95f))
        {
            throw new InvalidDataException($"progression.json mitigation '{name}' needs k in (0, 100000], perAttackerLevel in [0, 10000] and cap in (0, 0.95].");
        }
    }
}
