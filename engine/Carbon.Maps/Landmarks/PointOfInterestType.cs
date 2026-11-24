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
}
