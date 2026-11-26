// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;

namespace Carbon.Creatures.Scaling;

/// <summary>
/// Distance-to-level curve: contiguous <see cref="LevelBand"/>s, then linear growth beyond the last band.
/// </summary>
public sealed class LevelCurveConfig
{
    /// <summary>Gets the contiguous bands ordered by distance, starting at 0.</summary>
    public IReadOnlyList<LevelBand> Bands { get; set; } = [];

    /// <summary>Gets the levels gained per world unit beyond the last band.</summary>
    public float LevelsPerUnitBeyond { get; set; }

    /// <summary>Gets the maximum monster level.</summary>
    public int MaxLevel { get; set; } = 60;
}
