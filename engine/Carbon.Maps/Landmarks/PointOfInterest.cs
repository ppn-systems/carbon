// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Maps.Landmarks;

/// <summary>A generated point of interest: a settlement or landmark placed where the terrain suits it.</summary>
/// <param name="Type">Kind of point of interest.</param>
/// <param name="TileX">World tile X of its centre.</param>
/// <param name="TileY">World tile Y of its centre.</param>
/// <param name="Radius">Radius in tiles kept clear of natural props.</param>
/// <param name="FacingX">X of the unit direction the site faces (towards the sea for a harbour), or 0.</param>
/// <param name="FacingY">Y of the unit direction the site faces, or 0.</param>
public readonly record struct PointOfInterest(PointOfInterestType Type, int TileX, int TileY, int Radius, float FacingX = 0f, float FacingY = 0f)
{
    /// <summary>Returns whether a world tile lies inside the cleared footprint.</summary>
    /// <param name="worldTileX">World tile X.</param>
    /// <param name="worldTileY">World tile Y.</param>
    /// <returns><c>true</c> when the tile is within <see cref="Radius"/>.</returns>
    public bool Contains(int worldTileX, int worldTileY)
    {
        int dx = worldTileX - this.TileX;
        int dy = worldTileY - this.TileY;
        return (dx * dx) + (dy * dy) <= this.Radius * this.Radius;
    }
}
