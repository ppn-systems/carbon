// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using Carbon.Maps.Biomes;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
using Carbon.Maps.Streaming;
using Carbon.Maps.Topography;

namespace Carbon.Maps.Navigation;

/// <summary>
/// Spatial navigation grid resolving terrain walkability, pathfinding traversal costs,
/// natural obstacles, constructed buildings, cliffs, and ramp inclines.
/// Zero heap allocations during gameplay queries.
/// </summary>
public sealed class TileWalkabilityGrid : ITileWalkabilityGrid
{
    private readonly ChunkWorld2D _world;
    private readonly ConcurrentDictionary<(int X, int Y), RampDirection> _ramps = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="TileWalkabilityGrid"/> class.
    /// </summary>
    /// <param name="world">The backing chunk streaming world.</param>
    public TileWalkabilityGrid(ChunkWorld2D world) =>
        _world = world ?? throw new ArgumentNullException(nameof(world));

    /// <summary>
    /// Registers or updates an inclined ramp at the specified tile.
    /// </summary>
    public void SetRamp(int worldTileX, int worldTileY, RampDirection direction)
    {
        if (direction == RampDirection.None)
        {
            _ = _ramps.TryRemove((worldTileX, worldTileY), out _);
        }
        else
        {
            _ramps[(worldTileX, worldTileY)] = direction;
        }
    }

    /// <summary>
    /// Queries the ramp direction registered on the specified tile, resolving procedural ramps if not manually placed.
    /// </summary>
    public RampDirection GetRamp(int worldTileX, int worldTileY)
    {
        if (_ramps.TryGetValue((worldTileX, worldTileY), out RampDirection direction))
        {
            return direction;
        }

        MapTile tile = _world.GetTileAtWorld(worldTileX, worldTileY);
        if (tile.HeightLevel == 0 || tile.Biome is BiomeType.DeepOcean or BiomeType.ShallowWater)
        {
            return RampDirection.None;
        }

        return TopographyHelper.ResolveProceduralRamp(
            worldTileX,
            worldTileY,
            tile.HeightLevel,
            (x, y) => _world.GetTileAtWorld(x, y).HeightLevel,
            (x, y) => _world.GetTileAtWorld(x, y).Biome is BiomeType.DeepOcean or BiomeType.ShallowWater);
    }

    /// <inheritdoc/>
    public byte GetHeightLevel(int worldTileX, int worldTileY) =>
        _world.GetTileAtWorld(worldTileX, worldTileY).HeightLevel;

    /// <inheritdoc/>
    public TileWalkability GetWalkability(int worldTileX, int worldTileY)
    {
        // 1. Check constructed buildings (highest priority)
        if (_world.Buildings.TryGetStructure(worldTileX, worldTileY, out StructureTile structure))
        {
            if (structure.IsSolid)
            {
                return TileWalkability.Impassable;
            }

            if (structure.Category == StructureCategory.Floor)
            {
                return TileWalkability.WalkableFast;
            }
        }

        // 2. Check solid physical natural props (trees, ores, boulders)
        if (_world.HasSolidPropAt(worldTileX, worldTileY))
        {
            return TileWalkability.Impassable;
        }

        // 3. Check inclined ramp
        if (this.GetRamp(worldTileX, worldTileY) != RampDirection.None)
        {
            return TileWalkability.WalkableRamp;
        }

        // 4. Check natural terrain tile
        MapTile tile = _world.GetTileAtWorld(worldTileX, worldTileY);

        if (tile.Biome is BiomeType.DeepOcean)
        {
            return TileWalkability.Impassable;
        }

        if (tile.Biome is BiomeType.ShallowWater or BiomeType.MistySwamp)
        {
            return TileWalkability.WalkableSlow;
        }

        return TileWalkability.WalkableNormal;
    }

    /// <inheritdoc/>
    public float GetMoveCost(int worldTileX, int worldTileY)
    {
        TileWalkability walkability = this.GetWalkability(worldTileX, worldTileY);
        return walkability switch
        {
            TileWalkability.Impassable => float.PositiveInfinity,
            TileWalkability.WalkableFast => 0.82f,
            TileWalkability.WalkableRamp => 1.2f,
            TileWalkability.WalkableSlow => 1.7f,
            TileWalkability.WalkableNormal => 1.0f,
            _ => 1.0f
        };
    }

    /// <inheritdoc/>
    public bool IsWalkable(int worldTileX, int worldTileY) =>
        this.GetWalkability(worldTileX, worldTileY) != TileWalkability.Impassable;

    /// <inheritdoc/>
    public bool CanTraverse(int fromX, int fromY, int toX, int toY)
    {
        // Target tile must be walkable
        if (!this.IsWalkable(toX, toY))
        {
            return false;
        }

        // Check diagonal movement: prevent cutting through corners between two impassable tiles
        int dx = toX - fromX;
        int dy = toY - fromY;

        if (dx != 0 && dy != 0)
        {
            // Moving diagonally: check adjacent orthogonals
            bool adj1Walkable = this.IsWalkable(fromX + dx, fromY);
            bool adj2Walkable = this.IsWalkable(fromX, fromY + dy);

            if (!adj1Walkable && !adj2Walkable)
            {
                // Both adjacent cardinal tiles are impassable (corner cutting squeeze)
                return false;
            }
        }

        // Check height level transition and vertical cliff barriers
        byte fromHeight = this.GetHeightLevel(fromX, fromY);
        byte toHeight = this.GetHeightLevel(toX, toY);

        if (fromHeight != toHeight)
        {
            return CliffAutotiler.CanStepAcross(
                fromX,
                fromY,
                toX,
                toY,
                this.GetHeightLevel,
                this.GetRamp);
        }

        return true;
    }
}
