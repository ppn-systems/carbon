// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Maps.Settlements;

/// <summary>
/// A building placed by the village planner: which template, where its rotated footprint starts and how it is turned.
/// Part of <see cref="Landmarks.LandmarkLayout"/>, so it is a pure function of the world seed.
/// </summary>
/// <param name="TemplateIndex">Index into <see cref="VillageBuildingCatalog.Templates"/>.</param>
/// <param name="Role">Template role (copied for quick filtering).</param>
/// <param name="Style">Village style the building uses.</param>
/// <param name="OriginX">World tile X of the rotated footprint's top-left cell.</param>
/// <param name="OriginY">World tile Y of the rotated footprint's top-left cell.</param>
/// <param name="Turns">Clockwise quarter turns from the authored (entrance-south) orientation.</param>
/// <param name="Width">Rotated footprint width in tiles.</param>
/// <param name="Height">Rotated footprint height in tiles.</param>
public readonly record struct VillagePlot(
    ushort TemplateIndex,
    VillageBuildingRole Role,
    VillageStyle Style,
    int OriginX,
    int OriginY,
    byte Turns,
    byte Width,
    byte Height)
{
    /// <summary>Gets whether a world tile lies inside the footprint.</summary>
    public bool Contains(int worldTileX, int worldTileY) =>
        worldTileX >= this.OriginX && worldTileX < this.OriginX + this.Width &&
        worldTileY >= this.OriginY && worldTileY < this.OriginY + this.Height;

    /// <summary>Maps an authored template cell to its world tile.</summary>
    public (int X, int Y) ToWorld(VillageBuildingTemplate template, int cellX, int cellY)
    {
        System.ArgumentNullException.ThrowIfNull(template);
        (int rx, int ry) = template.Rotate(cellX, cellY, this.Turns);
        return (this.OriginX + rx, this.OriginY + ry);
    }
}
