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
    /// Gets the width-scaled distance to the nearest river or lake and whether that coordinate falls in an
    /// impassable deep-water channel core rather than a wadeable shoal or ford.
    /// </summary>
    /// <param name="worldX">World tile X.</param>
    /// <param name="worldY">World tile Y.</param>
    /// <param name="isDeepChannel">Outputs <see langword="true"/> when inside a deep navigable/impassable river core.</param>
    /// <returns>The width-scaled distance; <see cref="float.MaxValue"/> when far from any river.</returns>
    float GetDistance(int worldX, int worldY, out bool isDeepChannel);
}
