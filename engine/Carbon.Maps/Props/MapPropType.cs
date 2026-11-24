// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Maps.Props;

/// <summary>
/// Identifies natural physical and decorative environmental props scattered throughout procedural biomes.
/// </summary>
public enum MapPropType : byte
{
    /// <summary>
    /// No prop occupying this tile.
    /// </summary>
    None = 0,

    /// <summary>
    /// Broadleaf oak tree with dense foliage canopy (Lush Plains, Ancient Forest).
    /// </summary>
    OakTree = 1,

    /// <summary>
    /// Coniferous pine tree with needle foliage (Highlands, Ancient Forest).
    /// </summary>
    PineTree = 2,

    /// <summary>
    /// Snow-dusted glacial pine tree (Frozen Tundra).
    /// </summary>
    SnowPine = 3,

    /// <summary>
    /// Tropical palm tree with slender trunk and fronds (Beach Sand, Tropical Jungle).
    /// </summary>
    PalmTree = 4,

    /// <summary>
    /// Multi-branched desert saguaro cactus (Dune Desert, Red Mesa).
    /// </summary>
    Cactus = 5,

    /// <summary>
    /// Gnarled, leafless swamp cypress tree (Misty Swamp, Volcanic Ashland).
    /// </summary>
    DeadTree = 6,

    /// <summary>
    /// Impassable hexagonal basalt / obsidian volcanic column (Volcanic Ashland).
    /// </summary>
    BasaltPillar = 7,

    /// <summary>
    /// Solid natural rock boulder obstacle (All land biomes).
    /// </summary>
    Boulder = 8,

    /// <summary>
    /// Walkable cluster of wild flowers or tall grass (Lush Plains, Jungle).
    /// </summary>
    WildflowerPatch = 9,

    /// <summary>
    /// Ancient mystical stone shrine obelisk (Wilderness POI).
    /// </summary>
    AncientShrine = 10
}
