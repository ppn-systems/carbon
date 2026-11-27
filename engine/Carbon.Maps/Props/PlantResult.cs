// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Maps.Props;

/// <summary>The outcome of planting a sapling on a tile (ADR-040 D1).</summary>
public enum PlantResult : byte
{
    /// <summary>A sapling now grows on the tile.</summary>
    Planted = 0,

    /// <summary>The tile is not natural ground a tree grows on: water, rock, a road or a bridge, or a treeless biome.</summary>
    UnsuitableGround = 1,

    /// <summary>A structure, a construction site or another prop is on the tile.</summary>
    Occupied = 2,
}
