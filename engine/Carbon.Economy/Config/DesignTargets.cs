// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;

namespace Carbon.Economy.Config;

/// <summary>
/// Designed income bands (<c>economy.json → designTargets</c>). Not used at runtime; the economy simulation test
/// asserts the configured loot tables stay inside these bands so balance regressions fail CI.
/// </summary>
public sealed record DesignTargets
{
    /// <summary>Gets the assumed sustained kill rate of an engaged player.</summary>
    public int KillsPerHour { get; set; } = 120;

    /// <summary>Gets the accepted gold/hour from normal monsters by level band.</summary>
    public IReadOnlyList<LevelBand> GoldPerHour { get; set; } = [];

    /// <summary>Gets the accepted diamonds/hour from normal (non-elite) monsters by level band.</summary>
    public IReadOnlyList<LevelBand> DiamondsPerHour { get; set; } = [];

    /// <summary>Gets the accepted net gold/hour (all faucets minus all sinks) of the simulated player by level band.</summary>
    public IReadOnlyList<LevelBand> NetGoldPerHour { get; set; } = [];

    /// <summary>Gets the accepted share of faucet gold the sinks remove (<c>0..1</c>), by level band.</summary>
    public IReadOnlyList<LevelBand> SinkShare { get; set; } = [];

    /// <summary>
    /// Gets the accepted inflation slope: the least-squares slope of net gold/hour against level (gold/hour per level).
    /// </summary>
    public ValueRange InflationSlope { get; set; } = new() { Min = 0d, Max = double.MaxValue };
}
