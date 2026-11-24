// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Maps.Biomes;
using Carbon.Physics.Obstacles;

namespace Carbon.Maps.Data;

/// <summary>
/// Contiguous 2D grid of procedural map tiles.
/// </summary>
public sealed class TilemapData
{
    private readonly int _width;
    private readonly int _height;
    private readonly MapTile[] _tiles;

    /// <summary>
    /// Gets the horizontal map width in tiles.
    /// </summary>
    public int Width => _width;

    /// <summary>
    /// Gets the vertical map height in tiles.
    /// </summary>
    public int Height => _height;

    /// <summary>
    /// Gets the total number of tiles in the map.
    /// </summary>
    public int TotalTiles => _tiles.Length;

    /// <summary>
    /// Initializes a new instance of the <see cref="TilemapData"/> class.
    /// </summary>
    /// <param name="width">Map width in tiles.</param>
    /// <param name="height">Map height in tiles.</param>
    public TilemapData(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width, nameof(width));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height, nameof(height));

        _width = width;
        _height = height;
        _tiles = new MapTile[width * height];
    }

    /// <summary>
    /// Retrieves a tile at the specified coordinates.
    /// </summary>
    public ref readonly MapTile GetTile(int x, int y)
    {
        this.EnsureInBounds(x, y);
        return ref _tiles[(y * _width) + x];
    }

    /// <summary>
    /// Sets a tile at the specified coordinates.
    /// </summary>
    public void SetTile(int x, int y, in MapTile tile)
    {
        this.EnsureInBounds(x, y);
        _tiles[(y * _width) + x] = tile;
    }

    /// <summary>
    /// Converts the solid tiles of this map into a physical <see cref="TileObstacleGrid"/>.
    /// </summary>
    /// <param name="tileSize">Dimension of each tile in world units.</param>
    /// <returns>A populated obstacle grid.</returns>
    public TileObstacleGrid ToObstacleGrid(float tileSize = 32f)
    {
        TileObstacleGrid grid = new(_width, _height, tileSize);
        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                if (_tiles[(y * _width) + x].IsSolid)
                {
                    grid.SetSolid(x, y, true);
                }
            }
        }

        return grid;
    }

    /// <summary>
    /// Searches for a valid, walkable spawn coordinate in world units around the center of the map.
    /// </summary>
    /// <param name="tileSize">Physical dimension of each tile.</param>
    /// <param name="preferredBiome">Preferred starter biome.</param>
    /// <returns>World coordinate for spawning players or entities.</returns>
    public Vector2 FindSpawnPoint(float tileSize = 32f, BiomeType preferredBiome = BiomeType.LushPlains)
    {
        int centerX = _width / 2;
        int centerY = _height / 2;
        int maxRadius = Math.Max(_width, _height) / 2;

        // Spiral/concentric search outward from center
        for (int r = 0; r < maxRadius; r++)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Abs(dx) != r && Math.Abs(dy) != r)
                    {
                        continue;
                    }

                    int tx = centerX + dx;
                    int ty = centerY + dy;

                    if (tx >= 0 && tx < _width && ty >= 0 && ty < _height)
                    {
                        ref readonly MapTile tile = ref _tiles[(ty * _width) + tx];
                        if (!tile.IsSolid && tile.Biome == preferredBiome)
                        {
                            return new Vector2((tx + 0.5f) * tileSize, (ty + 0.5f) * tileSize);
                        }
                    }
                }
            }
        }

        // Fallback: any non-solid tile
        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                if (!_tiles[(y * _width) + x].IsSolid)
                {
                    return new Vector2((x + 0.5f) * tileSize, (y + 0.5f) * tileSize);
                }
            }
        }

        return new Vector2(tileSize * 0.5f, tileSize * 0.5f);
    }

    private void EnsureInBounds(int x, int y)
    {
        if (x < 0 || x >= _width || y < 0 || y >= _height)
        {
            throw new ArgumentOutOfRangeException(
                $"Tile coordinates ({x}, {y}) must be within [0, {_width - 1}] x [0, {_height - 1}].");
        }
    }
}
