// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Economy.Config;

/// <summary>
/// Inclusive monster level range with an accepted <c>[Min, Max]</c> value window (design target).
/// </summary>
public sealed record LevelBand
{
    /// <summary>Gets the first level of the band.</summary>
    public int MinLevel { get; set; }

    /// <summary>Gets the last level of the band.</summary>
    public int MaxLevel { get; set; }

    /// <summary>Gets the lowest accepted value.</summary>
    public double Min { get; set; }

    /// <summary>Gets the highest accepted value.</summary>
    public double Max { get; set; }
}
