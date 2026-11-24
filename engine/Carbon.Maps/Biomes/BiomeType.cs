// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Maps.Biomes;

/// <summary>
/// Identifies the ecological biome classification of a map tile.
/// </summary>
public enum BiomeType : byte
{
    /// <summary>Deep ocean water, impassable natural boundary.</summary>
    DeepOcean = 0,

    /// <summary>Shallow coastal water, walkable with speed penalty.</summary>
    ShallowWater = 1,

    /// <summary>Sandy coastline and beach.</summary>
    BeachSand = 2,

    /// <summary>Fertile grassy plains and meadows (starter territory).</summary>
    LushPlains = 3,

    /// <summary>Dense woodland with ancient trees and shade.</summary>
    AncientForest = 4,

    /// <summary>Murky wetlands and swamps with muddy slowness.</summary>
    MistySwamp = 5,

    /// <summary>Arid sandy desert with dunes and heat.</summary>
    DuneDesert = 6,

    /// <summary>Red clay canyons and rocky mesa plateaus.</summary>
    RedMesa = 7,

    /// <summary>Humid, overgrown tropical jungle.</summary>
    TropicalJungle = 8,

    /// <summary>Sub-zero snowfields and frozen tundra.</summary>
    FrozenTundra = 9,

    /// <summary>Scorched volcanic badlands with basalt and lava.</summary>
    VolcanicAshland = 10
}
