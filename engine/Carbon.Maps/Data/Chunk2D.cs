// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Physics.Obstacles;

namespace Carbon.Maps.Data;

/// <summary>
/// A uniform 32x32 tile chunk representing a local segment of the procedural world.
/// </summary>
public sealed class Chunk2D
{
    /// <summary>
    /// Dimension width and height of each chunk in tiles.
    /// </summary>
    public const int Size = 32;

    private readonly int _chunkX;
    private readonly int _chunkY;
    private readonly MapTile[] _tiles = new MapTile[Size * Size];
    private readonly System.Collections.Generic.List<Carbon.Maps.Props.MapProp> _props = new();

    /// <summary>
    /// Gets the list of physical and decorative props positioned within this chunk.
    /// </summary>
    public System.Collections.Generic.List<Carbon.Maps.Props.MapProp> Props => _props;

    /// <summary>
    /// Gets the horizontal chunk coordinate in chunk space.
    /// </summary>
    public int ChunkX => _chunkX;

    /// <summary>
    /// Gets the vertical chunk coordinate in chunk space.
    /// </summary>
    public int ChunkY => _chunkY;

    /// <summary>
    /// Initializes a new instance of the <see cref="Chunk2D"/> class.
    /// </summary>
    /// <param name="chunkX">Chunk X coordinate.</param>
    /// <param name="chunkY">Chunk Y coordinate.</param>
    public Chunk2D(int chunkX, int chunkY)
    {
        _chunkX = chunkX;
        _chunkY = chunkY;
    }

    /// <summary>
    /// Retrieves a tile at the local chunk coordinates [0..31, 0..31].
    /// </summary>
    public ref readonly MapTile GetTile(int localX, int localY)
    {
        EnsureInBounds(localX, localY);
        return ref _tiles[(localY * Size) + localX];
    }

    /// <summary>
    /// Sets a tile at the local chunk coordinates [0..31, 0..31].
    /// </summary>
    public void SetTile(int localX, int localY, in MapTile tile)
    {
        EnsureInBounds(localX, localY);
        _tiles[(localY * Size) + localX] = tile;
    }

    /// <summary>
    /// Appends a prop to this chunk's collection.
    /// </summary>
    /// <param name="prop">The prop to add.</param>
    public void AddProp(in Carbon.Maps.Props.MapProp prop) => _props.Add(prop);

    /// <summary>
    /// Clears all props from this chunk.
    /// </summary>
    public void ClearProps() => _props.Clear();

    /// <summary>
    /// Generates a local <see cref="TileObstacleGrid"/> from the solid tiles and physical props within this chunk.
    /// </summary>
    /// <param name="tileSize">Dimension of each tile in world units (e.g. 32f).</param>
    /// <returns>A populated obstacle grid.</returns>
    public TileObstacleGrid ToObstacleGrid(float tileSize = 32f)
    {
        TileObstacleGrid grid = new(Size, Size, tileSize);
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                if (_tiles[(y * Size) + x].IsSolid)
                {
                    grid.SetSolid(x, y, true);
                }
            }
        }

        for (int i = 0; i < _props.Count; i++)
        {
            Carbon.Maps.Props.MapProp p = _props[i];
            if (p.HasCollision)
            {
                int px = Math.Clamp(p.LocalTileX, 0, Size - 1);
                int py = Math.Clamp(p.LocalTileY, 0, Size - 1);
                grid.SetSolid(px, py, true);
            }
        }

        return grid;
    }

    private static void EnsureInBounds(int localX, int localY)
    {
        if ((uint)localX >= Size || (uint)localY >= Size)
        {
            throw new ArgumentOutOfRangeException(
                $"Chunk coordinates ({localX}, {localY}) must be within [0, {Size - 1}].");
        }
    }
}
