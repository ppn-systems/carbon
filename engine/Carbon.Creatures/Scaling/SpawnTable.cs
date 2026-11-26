// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Carbon.Maps.Biomes;

namespace Carbon.Creatures.Scaling;

/// <summary>
/// Weighted spawn table for one biome.
/// </summary>
public sealed class SpawnTable
{
    /// <summary>Gets the biome this table applies to.</summary>
    public BiomeType Biome { get; set; }

    /// <summary>Gets the weighted entries.</summary>
    public IReadOnlyList<SpawnTableEntry> Entries { get; set; } = [];
}
