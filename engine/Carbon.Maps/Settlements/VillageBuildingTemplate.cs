// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;

namespace Carbon.Maps.Settlements;

/// <summary>
/// One building of <c>village_buildings.json</c>: a footprint grid authored with its entrance on the south (bottom) row,
/// the furniture inside, and where its NPC stands. Grid cells: <c>W</c> wall, <c>D</c> door, <c>.</c> floor,
/// <c>p</c> porch, space = untouched ground.
/// </summary>
public sealed class VillageBuildingTemplate
{
    /// <summary>Cell of a wall.</summary>
    public const char WallCell = 'W';

    /// <summary>Cell of a door.</summary>
    public const char DoorCell = 'D';

    /// <summary>Cell of an interior floor.</summary>
    public const char FloorCell = '.';

    /// <summary>Cell of a porch / deck.</summary>
    public const char PorchCell = 'p';

    /// <summary>Cell left as natural ground.</summary>
    public const char EmptyCell = ' ';

    private readonly char[] _cells;

    internal VillageBuildingTemplate(
        string id,
        VillageBuildingRole role,
        int weight,
        int width,
        int height,
        char[] cells,
        IReadOnlyList<VillageFurniture> furniture,
        (int X, int Y)? npcSpot,
        IReadOnlySet<VillageStyle> styles)
    {
        this.Id = id;
        this.Role = role;
        this.Weight = weight;
        this.Width = width;
        this.Height = height;
        _cells = cells;
        this.Furniture = furniture;
        this.NpcSpot = npcSpot;
        this.Styles = styles;
    }

    /// <summary>Gets the stable id.</summary>
    public string Id { get; }

    /// <summary>Gets the role.</summary>
    public VillageBuildingRole Role { get; }

    /// <summary>Gets the selection weight among templates of the same role and style.</summary>
    public int Weight { get; }

    /// <summary>Gets the authored width in tiles.</summary>
    public int Width { get; }

    /// <summary>Gets the authored height in tiles.</summary>
    public int Height { get; }

    /// <summary>Gets the furniture in authored coordinates.</summary>
    public IReadOnlyList<VillageFurniture> Furniture { get; }

    /// <summary>Gets the authored cell the building's NPC stands on, if it has one.</summary>
    public (int X, int Y)? NpcSpot { get; }

    /// <summary>Gets the styles this template is built in.</summary>
    public IReadOnlySet<VillageStyle> Styles { get; }

    /// <summary>Gets the authored cell at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    /// <param name="x">Cell X.</param>
    /// <param name="y">Cell Y.</param>
    /// <returns>The cell character, or <see cref="EmptyCell"/> outside the grid.</returns>
    public char CellAt(int x, int y) =>
        (uint)x < (uint)this.Width && (uint)y < (uint)this.Height ? _cells[(y * this.Width) + x] : EmptyCell;

    /// <summary>Gets whether this template can be built in <paramref name="style"/>.</summary>
    public bool Supports(VillageStyle style) => this.Styles.Contains(style);

    /// <summary>Gets the rotated size for <paramref name="turns"/> clockwise quarter turns.</summary>
    public (int Width, int Height) RotatedSize(int turns) =>
        (turns & 1) == 0 ? (this.Width, this.Height) : (this.Height, this.Width);

    /// <summary>
    /// Maps an authored cell to the cell of the template rotated by <paramref name="turns"/> clockwise quarter turns.
    /// One turn moves the south-facing entrance to the west side, two to the north, three to the east.
    /// </summary>
    public (int X, int Y) Rotate(int x, int y, int turns)
    {
        int w = this.Width;
        int h = this.Height;
        return (turns & 3) switch
        {
            0 => (x, y),
            1 => (h - 1 - y, x),
            2 => (w - 1 - x, h - 1 - y),
            _ => (y, w - 1 - x),
        };
    }

    internal static void ValidateGrid(string id, IReadOnlyList<string> rows, out int width, out int height, out char[] cells)
    {
        height = rows.Count;
        width = height == 0 ? 0 : rows[0].Length;
        if (width is < 3 or > 15 || height is < 1 or > 15)
        {
            throw new FormatException($"Village building '{id}' grid must be 3..15 wide and 1..15 tall.");
        }

        cells = new char[width * height];
        for (int y = 0; y < height; y++)
        {
            if (rows[y].Length != width)
            {
                throw new FormatException($"Village building '{id}' grid rows must all have the same length.");
            }

            for (int x = 0; x < width; x++)
            {
                char c = rows[y][x];
                if (c is not (WallCell or DoorCell or FloorCell or PorchCell or EmptyCell))
                {
                    throw new FormatException($"Village building '{id}' grid has unknown cell '{c}'.");
                }

                cells[(y * width) + x] = c;
            }
        }
    }
}
