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
}
