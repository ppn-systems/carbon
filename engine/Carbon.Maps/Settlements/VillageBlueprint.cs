// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Maps.Building;

namespace Carbon.Maps.Settlements;

/// <summary>Expands village plots into the structure tiles they put on the building grid.</summary>
public static class VillageBlueprint
{
    /// <summary>
    /// Appends every structure tile of <paramref name="plot"/>: walls, doors, floors and porches from the style palette,
    /// then the furniture (each footprint tile of it, standing on the floor of its cell).
    /// </summary>
    /// <param name="catalog">Village style palettes.</param>
    /// <param name="structures">Structure definitions (furniture footprints).</param>
    /// <param name="plot">The plot.</param>
    /// <param name="output">Receives the placements (not cleared).</param>
    public static void Expand(VillageBuildingCatalog catalog, StructureCatalog structures, in VillagePlot plot, List<VillagePlacement> output)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(structures);
        ArgumentNullException.ThrowIfNull(output);
        VillageBuildingTemplate template = plot.Template;
        VillageStylePalette palette = catalog.Palette(plot.Style);
        int start = output.Count;

        for (int y = 0; y < template.Height; y++)
        {
            for (int x = 0; x < template.Width; x++)
            {
                StructureType type = CellType(template.CellAt(x, y), palette);
                if (type == StructureType.None)
                {
                    continue;
                }

                (int wx, int wy) = plot.ToWorld(x, y);
                output.Add(new VillagePlacement(wx, wy, type, StructureRotation.North, wx, wy, StructureType.None));
            }
        }

        int baseCount = output.Count;
        foreach (VillageFurniture f in template.Furniture)
        {
            (int fw, int fh) = structures.TryGet(f.Type, out StructureDefinition def) ? def.GetFootprint(f.Rotation) : (1, 1);
            (int ax, int ay) = plot.ToWorld(f.X, f.Y);
            (int bx, int by) = plot.ToWorld(f.X + fw - 1, f.Y + fh - 1);
            int minX = Math.Min(ax, bx);
            int minY = Math.Min(ay, by);
            int maxX = Math.Max(ax, bx);
            int maxY = Math.Max(ay, by);
            StructureRotation rotation = (StructureRotation)(((int)f.Rotation + plot.Turns) & 3);
            for (int ty = minY; ty <= maxY; ty++)
            {
                for (int tx = minX; tx <= maxX; tx++)
                {
                    StructureType floor = StructureType.None;
                    for (int i = start; i < baseCount; i++)
                    {
                        if (output[i].X == tx && output[i].Y == ty)
                        {
                            floor = output[i].Type;
                            output[i] = output[i] with { Type = StructureType.None };
                            break;
                        }
                    }

                    output.Add(new VillagePlacement(tx, ty, f.Type, rotation, minX, minY, floor));
                }
            }
        }

        // Floors covered by furniture were marked None above; drop them (the furniture records the floor beneath).
        for (int i = baseCount - 1; i >= start; i--)
        {
            if (output[i].Type == StructureType.None)
            {
                output.RemoveAt(i);
            }
        }
    }

    /// <summary>Gets the world tile of the plot's NPC spot, if its template has one.</summary>
    public static bool TryGetNpcTile(in VillagePlot plot, out int tileX, out int tileY)
    {
        VillageBuildingTemplate template = plot.Template;
        if (template.NpcSpot is (int x, int y))
        {
            (tileX, tileY) = plot.ToWorld(x, y);
            return true;
        }

        tileX = 0;
        tileY = 0;
        return false;
    }

    /// <summary>
    /// Gets the world tile of the entrance: the door on the authored south wall (searched from the bottom row up), or the
    /// front centre for door-less templates.
    /// </summary>
    public static (int X, int Y) EntranceTile(in VillagePlot plot)
    {
        VillageBuildingTemplate template = plot.Template;
        for (int y = template.Height - 1; y >= 0; y--)
        {
            for (int x = 0; x < template.Width; x++)
            {
                if (template.CellAt(x, y) == VillageBuildingTemplate.DoorCell)
                {
                    return plot.ToWorld(x, y);
                }
            }
        }

        return plot.ToWorld(template.Width / 2, template.Height - 1);
    }

    /// <summary>Gets the world tile just outside the entrance (where the village path ends).</summary>
    public static (int X, int Y) FrontTile(in VillagePlot plot)
    {
        (int ex, int ey) = EntranceTile(plot);
        return (plot.Turns & 3) switch
        {
            0 => (ex, ey + 1),
            1 => (ex - 1, ey),
            2 => (ex, ey - 1),
            _ => (ex + 1, ey),
        };
    }

    private static StructureType CellType(char cell, in VillageStylePalette palette) => cell switch
    {
        VillageBuildingTemplate.WallCell => palette.Wall,
        VillageBuildingTemplate.DoorCell => palette.Door,
        VillageBuildingTemplate.FloorCell => palette.Floor,
        VillageBuildingTemplate.PorchCell => palette.Porch,
        _ => StructureType.None,
    };
}
