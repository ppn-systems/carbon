// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Maps.Landmarks;

/// <summary>Kinds of generated points of interest. Append only: values are persisted and sent over the wire.</summary>
public enum PointOfInterestType : byte
{
    /// <summary>No point of interest.</summary>
    None = 0,

    /// <summary>Farming village on flat, fertile lowland a short walk from a river.</summary>
    RiverVillage = 1,

    /// <summary>Fishing harbour on the coast next to a river mouth.</summary>
    Harbor = 2,

    /// <summary>Shrine on a mountain summit.</summary>
    MountainShrine = 3,

    /// <summary>Camp on the shore of a lake or a large river.</summary>
    LakesideCamp = 4,

    /// <summary>Adobe village beside a river crossing the desert (an oasis settlement).</summary>
    DesertVillage = 5,

    /// <summary>Log-cabin village on frozen tundra near water.</summary>
    SnowVillage = 6,

    /// <summary>Stilt-house village in a misty swamp.</summary>
    SwampVillage = 7,

    /// <summary>A rare colossal World Tree in an old forest or meadow, ringed by rune stones.</summary>
    WorldTree = 8,

    /// <summary>A mine dug into a plateau in mountain or badland country, with an ore cart and spoil heaps.</summary>
    MineEntrance = 9,

    /// <summary>Weather-worn ancient stone ruins in wilds, deserts, and forests.</summary>
    AncientRuins = 10,

    /// <summary>Fortified outlaw bandit camp with tents, barrels, and a lookout watchtower.</summary>
    BanditCamp = 11,

    /// <summary>Roadside wooden watchtower overlooking frontier plains, mesas, or tundra.</summary>
    Watchtower = 12,

    /// <summary>Large ringed stone arena in dangerous outer regions designed for world boss encounters.</summary>
    BossArena = 13,
}
