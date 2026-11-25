// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Numerics;

namespace Carbon.Maps.Building;

/// <summary>
/// Domain value object encapsulating the geometry, boundary, and indoor classification of a detected room.
/// Follows RimWorld colony room metrics.
/// </summary>
public sealed class RoomEnclosure
{
    private readonly HashSet<(int X, int Y)> _tileLookup;
    private readonly IReadOnlyList<(int X, int Y)> _tiles;
    private readonly bool _isEnclosed;
    private readonly int _minTileX;
    private readonly int _minTileY;
    private readonly int _maxTileX;
    private readonly int _maxTileY;

    /// <summary>
    /// Gets a value indicating whether this interior space is completely enclosed by solid walls and doors (Indoor Room).
    /// If false, the area connects to the unbounded wilderness (Outdoors).
    /// </summary>
    public bool IsEnclosed => _isEnclosed;

    /// <summary>
    /// Gets the total number of interior floor tiles within this room.
    /// </summary>
    public int TileCount => _tiles.Count;

    /// <summary>
    /// Gets the list of interior tile coordinates belonging to this room.
    /// </summary>
    public IReadOnlyList<(int X, int Y)> Tiles => _tiles;

    /// <summary>
    /// Gets the minimum horizontal tile coordinate bounding this room.
    /// </summary>
    public int MinTileX => _minTileX;

    /// <summary>
    /// Gets the minimum vertical tile coordinate bounding this room.
    /// </summary>
    public int MinTileY => _minTileY;

    /// <summary>
    /// Gets the maximum horizontal tile coordinate bounding this room.
    /// </summary>
    public int MaxTileX => _maxTileX;

    /// <summary>
    /// Gets the maximum vertical tile coordinate bounding this room.
    /// </summary>
    public int MaxTileY => _maxTileY;

    /// <summary>
    /// Initializes a new instance of the <see cref="RoomEnclosure"/> class.
    /// </summary>
    public RoomEnclosure(
        IReadOnlyList<(int X, int Y)> tiles,
        bool isEnclosed,
        int minTileX,
        int minTileY,
        int maxTileX,
        int maxTileY)
    {
        _tiles = tiles;
        _isEnclosed = isEnclosed;
        _minTileX = minTileX;
        _minTileY = minTileY;
        _maxTileX = maxTileX;
        _maxTileY = maxTileY;
        _tileLookup = [.. tiles];
    }

    /// <summary>
    /// Evaluates whether the specified tile coordinate is situated inside this room.
    /// </summary>
    public bool ContainsTile(int x, int y) =>
        _tileLookup.Contains((x, y));

    /// <summary>
    /// Evaluates whether a continuous world position falls inside the interior bounds of this room.
    /// </summary>
    public bool ContainsWorldPosition(Vector2 position, float tileSize)
    {
        int tx = (int)System.MathF.Floor(position.X / tileSize);
        int ty = (int)System.MathF.Floor(position.Y / tileSize);
        return this.ContainsTile(tx, ty);
    }

    /// <summary>
    /// Creates an empty or outdoor open air room representation.
    /// </summary>
    public static RoomEnclosure Outdoors =>
        new([], false, 0, 0, 0, 0);
}
