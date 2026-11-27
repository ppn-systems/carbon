// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Maps.Hydrology;

/// <summary>Terrain elevation used to route rivers; must be deterministic.</summary>
public interface IRoutingTerrain
{
    /// <summary>Samples the routing elevation (0..1) at a world tile.</summary>
    /// <param name="worldX">World tile X.</param>
    /// <param name="worldY">World tile Y.</param>
    /// <returns>Elevation before river valleys are carved.</returns>
    float SampleRoutingElevation(int worldX, int worldY);
}

/// <summary>Width-scaled distance from a tile to the nearest river or lake (≤ 2.2 is water).</summary>
public interface IRiverDistanceSource
{
    /// <summary>Gets the width-scaled distance to the nearest river or lake.</summary>
    /// <param name="worldX">World tile X.</param>
    /// <param name="worldY">World tile Y.</param>
    /// <returns>The distance; <see cref="float.MaxValue"/> when far from any river.</returns>
    float GetDistance(int worldX, int worldY);

    /// <summary>
    /// Gets the width-scaled distance to the nearest river or lake and, on its water, how deep it is there
    /// (see <see cref="WaterDepth"/>): a river is as deep as it is wide, deepest mid-stream.
    /// </summary>
    /// <param name="worldX">World tile X.</param>
    /// <param name="worldY">World tile Y.</param>
    /// <param name="waterDepth">The water depth when the distance is at most <see cref="WorldConstants.RiverChannel"/>; otherwise 0.</param>
    /// <returns>The width-scaled distance; <see cref="float.MaxValue"/> when far from any river.</returns>
    float GetDistance(int worldX, int worldY, out float waterDepth);

    /// <summary>Gets the distance from a sea tile to the nearest coast, in tiles: the sea deepens with it.</summary>
    /// <param name="worldX">World tile X.</param>
    /// <param name="worldY">World tile Y.</param>
    /// <returns>The distance in tiles, capped at a few hundred.</returns>
    float GetCoastDistance(int worldX, int worldY);
}
