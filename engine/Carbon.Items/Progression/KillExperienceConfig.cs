// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Items.Progression;

/// <summary>
/// Monster kill experience: <c>clamp(MaxHealthFactor * maxHp + LevelFactor * level, MinXp, MaxXp)</c>, times
/// <see cref="EliteMultiplier"/> for elites, <see cref="NightMultiplier"/> for night spawns and the level-difference
/// multiplier.
/// </summary>
public sealed class KillExperienceConfig
{
    /// <summary>Gets the experience per point of the monster's scaled max health.</summary>
    public float MaxHealthFactor { get; init; } = 0.45f;

    /// <summary>Gets the experience per monster level.</summary>
    public float LevelFactor { get; init; } = 4f;

    /// <summary>Gets the lower clamp of the base experience.</summary>
    public float MinXp { get; init; } = 25f;

    /// <summary>Gets the upper clamp of the base experience.</summary>
    public float MaxXp { get; init; } = 4000f;

    /// <summary>Gets the elite multiplier.</summary>
    public float EliteMultiplier { get; init; } = 2f;

    /// <summary>Gets the night-spawn multiplier.</summary>
    public float NightMultiplier { get; init; } = 1.35f;

    /// <summary>Gets the level-difference scaling.</summary>
    public LevelDifferenceConfig LevelDifference { get; init; } = new();
}
