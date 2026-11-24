// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Maps.Building;

/// <summary>
/// Domain value enum classifying the load-bearing capacity of a terrain tile.
/// Follows RimWorld architectural specifications to govern what structures can be constructed on a given biome.
/// Zero heap allocations.
/// </summary>
public enum TerrainAffordance : byte
{
    /// <summary>
    /// Deep water, chasms, or void. Cannot support any construction.
    /// </summary>
    None = 0,

    /// <summary>
    /// Shallow water, loose sand, mud, or marsh. Can support light wooden boardwalks or floors, but no heavy walls.
    /// </summary>
    Light = 1,

    /// <summary>
    /// Soil, dirt, clay, or gravel. Can support wooden walls, doors, light roofs, and small furniture.
    /// </summary>
    Medium = 2,

    /// <summary>
    /// Solid ground, compacted rock, bedrock, stone pavers. Can support heavy stone/metal walls and heavy overhead roofs.
    /// </summary>
    Heavy = 3
}
