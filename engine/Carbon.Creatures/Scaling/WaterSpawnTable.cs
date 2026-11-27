// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Carbon.Maps.Hydrology;

namespace Carbon.Creatures.Scaling;

/// <summary>
/// Weighted spawn table for one water depth tier (ADR-032 D14): spawn points in water of <see cref="Tier"/> pick from
/// it instead of a biome table. Every entry must be a water creature whose habitat contains the tier.
/// </summary>
public sealed class WaterSpawnTable
{
    /// <summary>Gets the depth tier this table applies to (Shore to Abyss).</summary>
    public WaterDepthTier Tier { get; set; }

    /// <summary>Gets the weighted entries.</summary>
    public IReadOnlyList<SpawnTableEntry> Entries { get; set; } = [];
}
