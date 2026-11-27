// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Items.Progression;

/// <summary>
/// Kill experience scaling by <c>monsterLevel - playerLevel</c>: monsters more than <see cref="GraceLevels"/> below the
/// player lose <see cref="PenaltyPerLevel"/> per extra level (floored at <see cref="MinMultiplier"/>); monsters above the
/// player give <see cref="BonusPerLevel"/> per level (capped at <see cref="MaxMultiplier"/>).
/// </summary>
public sealed class LevelDifferenceConfig
{
    /// <summary>Gets the levels below the player that still pay full experience.</summary>
    public int GraceLevels { get; init; } = 5;

    /// <summary>Gets the penalty per level beyond the grace window.</summary>
    public float PenaltyPerLevel { get; init; } = 0.1f;

    /// <summary>Gets the lowest multiplier for far lower-level monsters.</summary>
    public float MinMultiplier { get; init; } = 0.1f;

    /// <summary>Gets the bonus per level the monster is above the player.</summary>
    public float BonusPerLevel { get; init; } = 0.05f;

    /// <summary>Gets the highest multiplier for higher-level monsters.</summary>
    public float MaxMultiplier { get; init; } = 1.25f;
}
