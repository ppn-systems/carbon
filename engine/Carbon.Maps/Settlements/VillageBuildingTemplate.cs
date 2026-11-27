// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;

namespace Carbon.Maps.Settlements;

/// <summary>
/// The footprint of one village building, made by <see cref="VillageBuildingGenerator"/>: a cell grid with its entrance
/// on the south (bottom) side, the furniture inside, and where its NPC stands. Grid cells: <c>W</c> wall, <c>D</c> door,
/// <c>.</c> floor, <c>p</c> porch, <c>y</c> yard paving (an open work yard or a shop frontage), <c>P</c> roof pillar,
/// space = untouched ground (ADR-027).
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

    /// <summary>Cell of open-air paving outside the walls (the smithy's work yard, a shop's frontage).</summary>
    public const char YardCell = 'y';

    /// <summary>Cell of a roof pillar (the corners of an open-sided work yard).</summary>
    public const char PillarCell = 'P';

    /// <summary>Cell left as natural ground.</summary>
    public const char EmptyCell = ' ';

    private readonly char[] _cells;

    internal VillageBuildingTemplate(
        string id,
        VillageBuildingRole role,
        int width,
        int height,
        char[] cells,
        IReadOnlyList<VillageFurniture> furniture,
        (int X, int Y)? npcSpot,
        int roomCount)
    {
        if (cells.Length != width * height)
        {
            throw new ArgumentException("The cell grid does not match the size.", nameof(cells));
        }

        this.Id = id;
        this.Role = role;
        this.Width = width;
        this.Height = height;
        _cells = cells;
        this.Furniture = furniture;
        this.NpcSpot = npcSpot;
        this.RoomCount = roomCount;
    }

    /// <summary>Gets a readable id (role, size and shape), for logs and test messages.</summary>
    public string Id { get; }

    /// <summary>Gets the role.</summary>
    public VillageBuildingRole Role { get; }

    /// <summary>Gets the authored width in tiles.</summary>
    public int Width { get; }

    /// <summary>Gets the authored height in tiles.</summary>
    public int Height { get; }

    /// <summary>Gets the furniture in authored coordinates.</summary>
    public IReadOnlyList<VillageFurniture> Furniture { get; }

    /// <summary>Gets the authored cell the building's NPC stands on, if it has one.</summary>
    public (int X, int Y)? NpcSpot { get; }

    /// <summary>Gets the number of rooms (0 for an open stall).</summary>
    public int RoomCount { get; }

    /// <summary>Gets the authored cell at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    /// <param name="x">Cell X.</param>
    /// <param name="y">Cell Y.</param>
    /// <returns>The cell character, or <see cref="EmptyCell"/> outside the grid.</returns>
    public char CellAt(int x, int y) =>
        (uint)x < (uint)this.Width && (uint)y < (uint)this.Height ? _cells[(y * this.Width) + x] : EmptyCell;

    /// <summary>Gets whether another template has exactly the same footprint (size and every cell).</summary>
    /// <param name="other">The other template.</param>
    /// <returns><c>true</c> when the two grids are identical.</returns>
    public bool HasSameFootprint(VillageBuildingTemplate other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return this.Width == other.Width && this.Height == other.Height && _cells.AsSpan().SequenceEqual(other._cells);
    }

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
}
