// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Maps.Settlements;

/// <summary>
/// A building placed by the village planner: its generated footprint, where the rotated footprint starts and how it is
/// turned. Part of <see cref="Landmarks.LandmarkLayout"/>, so it is a pure function of the world seed (ADR-027).
/// </summary>
/// <param name="Template">The generated footprint, furniture and NPC spot.</param>
/// <param name="Role">Template role (copied for quick filtering).</param>
/// <param name="Style">Village style the building uses.</param>
/// <param name="OriginX">World tile X of the rotated footprint's top-left cell.</param>
/// <param name="OriginY">World tile Y of the rotated footprint's top-left cell.</param>
/// <param name="Turns">Clockwise quarter turns from the authored (entrance-south) orientation.</param>
/// <param name="Width">Rotated footprint width in tiles.</param>
/// <param name="Height">Rotated footprint height in tiles.</param>
public readonly record struct VillagePlot(
    VillageBuildingTemplate Template,
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
    public (int X, int Y) ToWorld(int cellX, int cellY)
    {
        (int rx, int ry) = this.Template.Rotate(cellX, cellY, this.Turns);
        return (this.OriginX + rx, this.OriginY + ry);
    }

    /// <summary>Gets the authored cell that lands on a world tile (<see cref="VillageBuildingTemplate.EmptyCell"/> outside).</summary>
    public char CellAtWorld(int worldTileX, int worldTileY)
    {
        if (!this.Contains(worldTileX, worldTileY))
        {
            return VillageBuildingTemplate.EmptyCell;
        }

        int rx = worldTileX - this.OriginX;
        int ry = worldTileY - this.OriginY;
        int w = this.Template.Width;
        int h = this.Template.Height;
        (int x, int y) = (this.Turns & 3) switch
        {
            0 => (rx, ry),
            1 => (ry, h - 1 - rx),
            2 => (w - 1 - rx, h - 1 - ry),
            _ => (w - 1 - ry, rx),
        };
        return this.Template.CellAt(x, y);
    }
}
