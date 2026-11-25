// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Maps.Data;

namespace Carbon.Maps.Generators.Stages;

/// <summary>
/// The working state of one tile as it flows through the tile stages. Each stage reads the fields
/// written by the stages before it and fills in its own.
/// </summary>
public struct TileSample
{
    /// <summary>World tile X.</summary>
    public int WorldX { get; set; }

    /// <summary>World tile Y.</summary>
    public int WorldY { get; set; }

    /// <summary>Noise-space X (world X × frequency).</summary>
    public float Fx { get; set; }

    /// <summary>Noise-space Y (world Y × frequency).</summary>
    public float Fy { get; set; }

    /// <summary>Detailed elevation (0..1), lowered by river valleys in the hydrology stage.</summary>
    public float Elevation { get; set; }

    /// <summary>Elevation before river valleys are carved: decides what counts as coast.</summary>
    public float CoastElevation { get; set; }

    /// <summary>Smooth, low-detail relief driving the discrete height levels.</summary>
    public float Relief { get; set; }

    /// <summary>Temperature (0..1).</summary>
    public float Temperature { get; set; }

    /// <summary>Moisture before river and coast influence.</summary>
    public float RawMoisture { get; set; }

    /// <summary>Final moisture (0..1).</summary>
    public float Moisture { get; set; }

    /// <summary>Width-scaled distance to the nearest river or lake; NaN on open ocean, where it is not needed.</summary>
    public float RiverDistance { get; set; }

    /// <summary>The resolved tile, once a stage finalises it.</summary>
    public MapTile Tile { get; set; }

    /// <summary>Gets a value indicating whether the tile is water or right next to it.</summary>
    public readonly bool IsNearWater => this.RiverDistance < WorldConstants.NearWater || this.Tile.Elevation < WorldConstants.CoastLevel;
}
