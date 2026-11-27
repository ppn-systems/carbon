// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using Carbon.Maps.Biomes;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
using Carbon.Maps.Hydrology;
using Carbon.Maps.Streaming;
using Carbon.Maps.Topography;
using Carbon.Physics.Obstacles;

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
    private readonly Func<int, int, byte> _heightQuery;
    private readonly Func<int, int, bool> _waterQuery;
    private readonly Func<int, int, byte> _heightLevelQuery;
    private readonly Func<int, int, RampDirection> _rampQuery;
    private readonly WaterBoundGrid?[] _waterGrids = new WaterBoundGrid?[32];

    /// <summary>
    /// Initializes a new instance of the <see cref="TileWalkabilityGrid"/> class.
    /// </summary>
    /// <param name="world">The backing chunk streaming world.</param>
    public TileWalkabilityGrid(ChunkWorld2D world)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));

        // Cached once: creating these lambdas per GetRamp/CanTraverse call allocated on every collision query.
        _heightQuery = (x, y) => _world.PeekTileAtWorld(x, y).HeightLevel;
        _waterQuery = (x, y) => _world.PeekTileAtWorld(x, y).Biome is BiomeType.DeepOcean or BiomeType.ShallowWater;
        _heightLevelQuery = this.GetHeightLevel;
        _rampQuery = this.GetRamp;
    }

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

        if (_world.Generator.Roads.TryGetRoadRamp(worldTileX, worldTileY, out RampDirection roadRamp))
        {
            return roadRamp;
        }

        MapTile tile = _world.PeekTileAtWorld(worldTileX, worldTileY);
        if (tile.HeightLevel == 0 || tile.Biome is BiomeType.DeepOcean or BiomeType.ShallowWater)
        {
            return RampDirection.None;
        }

        return TopographyHelper.ResolveProceduralRamp(
            worldTileX,
            worldTileY,
            tile.HeightLevel,
            _heightQuery,
            _waterQuery,
            IsRiverside(_world.Generator.GetRiverDistance(worldTileX, worldTileY)));
    }

    /// <inheritdoc/>
    public byte GetHeightLevel(int worldTileX, int worldTileY) =>
        _world.PeekTileAtWorld(worldTileX, worldTileY).HeightLevel;

    /// <inheritdoc/>
    public TileWalkability GetWalkability(int worldTileX, int worldTileY) =>
        this.GetWalkability(worldTileX, worldTileY, includeLandmarks: true);

    /// <summary>
    /// Evaluates walkability, optionally ignoring the structures of points of interest. Entity collision
    /// ignores them here because it resolves their walls precisely (with door openings), whereas
    /// pathfinding treats each building as one solid block.
    /// </summary>
    /// <param name="worldTileX">World tile X.</param>
    /// <param name="worldTileY">World tile Y.</param>
    /// <param name="includeLandmarks">Whether buildings and other landmark structures block the tile.</param>
    /// <returns>The walkability classification.</returns>
    public TileWalkability GetWalkability(int worldTileX, int worldTileY, bool includeLandmarks)
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

        // 2. Structures of villages, harbours, camps and shrines
        if (includeLandmarks && _world.Generator.Landmarks.IsTileBlocked(worldTileX, worldTileY))
        {
            return TileWalkability.Impassable;
        }

        // 3. Check solid physical natural props (trees, ores, boulders)
        if (_world.HasSolidPropAt(worldTileX, worldTileY))
        {
            return TileWalkability.Impassable;
        }

        // 3. Bridges over rivers and walkable wooden harbor piers
        if (_world.IsBridgeAtWorld(worldTileX, worldTileY) || _world.Generator.Landmarks.IsWalkablePierTile(worldTileX, worldTileY))
        {
            return TileWalkability.WalkableFast;
        }

        // 4. Inclined ramp
        if (this.GetRamp(worldTileX, worldTileY) != RampDirection.None)
        {
            return TileWalkability.WalkableRamp;
        }

        // 5. Natural terrain & roads
        MapTile tile = _world.PeekTileAtWorld(worldTileX, worldTileY);

        if (tile.IsSolid || tile.Biome is BiomeType.DeepOcean)
        {
            return TileWalkability.Impassable;
        }

        if (_world.IsRoadAtWorld(worldTileX, worldTileY))
        {
            return TileWalkability.WalkableFast;
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
    public ITileWalkabilityGrid ForTerrainMask(uint terrainMask)
    {
        if (!TerrainMask.IsWaterBound(terrainMask))
        {
            return this;
        }

        int key = (int)((terrainMask & TerrainMask.WaterTiers) >> 24);
        return _waterGrids[key] ??= new WaterBoundGrid(this, terrainMask & TerrainMask.WaterTiers);
    }

    /// <inheritdoc/>
    public bool IsWalkable(int worldTileX, int worldTileY) =>
        this.GetWalkability(worldTileX, worldTileY) != TileWalkability.Impassable;

    /// <inheritdoc/>
    public bool CanTraverse(int fromX, int fromY, int toX, int toY) =>
        this.CanTraverse(fromX, fromY, toX, toY, includeLandmarks: true);

    /// <summary>
    /// Evaluates whether movement between two tiles is allowed, optionally ignoring landmark structures
    /// (see <see cref="GetWalkability(int, int, bool)"/>).
    /// </summary>
    /// <param name="fromX">Source tile X.</param>
    /// <param name="fromY">Source tile Y.</param>
    /// <param name="toX">Target tile X.</param>
    /// <param name="toY">Target tile Y.</param>
    /// <param name="includeLandmarks">Whether buildings and other landmark structures block tiles.</param>
    /// <returns><c>true</c> when the move is allowed.</returns>
    public bool CanTraverse(int fromX, int fromY, int toX, int toY, bool includeLandmarks) =>
        this.CanTraverse(fromX, fromY, toX, toY, includeLandmarks, canSwim: false);

    /// <summary>
    /// Evaluates whether movement between two tiles is allowed for a walker or, with <paramref name="canSwim"/>, for a
    /// swimmer, to whom deep water is open (ADR-032 D10). Height steps and cliffs apply to both.
    /// </summary>
    /// <param name="fromX">Source tile X.</param>
    /// <param name="fromY">Source tile Y.</param>
    /// <param name="toX">Target tile X.</param>
    /// <param name="toY">Target tile Y.</param>
    /// <param name="includeLandmarks">Whether buildings and other landmark structures block tiles.</param>
    /// <param name="canSwim">Whether the mover swims and dives.</param>
    /// <returns><c>true</c> when the move is allowed.</returns>
    public bool CanTraverse(int fromX, int fromY, int toX, int toY, bool includeLandmarks, bool canSwim) =>
        this.CanTraverse(fromX, fromY, toX, toY, includeLandmarks, canSwim ? ~TerrainMask.DeepWater : uint.MaxValue);

    /// <summary>
    /// Evaluates whether a collider with <paramref name="terrainMask"/> (see <see cref="TerrainMask"/>) may move
    /// between two tiles: a walker as before, a swimmer through deep water too, and a water creature only through the
    /// depth tiers it lives in (ADR-032 D14). Height steps and cliffs apply to all of them.
    /// </summary>
    /// <param name="fromX">Source tile X.</param>
    /// <param name="fromY">Source tile Y.</param>
    /// <param name="toX">Target tile X.</param>
    /// <param name="toY">Target tile Y.</param>
    /// <param name="includeLandmarks">Whether buildings and other landmark structures block tiles.</param>
    /// <param name="terrainMask">The mover's collision mask.</param>
    /// <returns><c>true</c> when the move is allowed.</returns>
    public bool CanTraverse(int fromX, int fromY, int toX, int toY, bool includeLandmarks, uint terrainMask)
    {
        // Target tile must be walkable
        if (!this.IsOpen(toX, toY, includeLandmarks, terrainMask))
        {
            return false;
        }

        // Check diagonal movement: prevent cutting through corners between two impassable tiles
        int dx = toX - fromX;
        int dy = toY - fromY;

        if (dx != 0 && dy != 0)
        {
            // Moving diagonally: check adjacent orthogonals
            bool adj1Walkable = this.IsOpen(fromX + dx, fromY, includeLandmarks, terrainMask);
            bool adj2Walkable = this.IsOpen(fromX, fromY + dy, includeLandmarks, terrainMask);

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
                _heightLevelQuery,
                _rampQuery);
        }

        return true;
    }

    /// <summary>
    /// Gets whether a collider with <paramref name="terrainMask"/> may stand on a tile: walkable ground, deep water for
    /// a swimmer, or one of its own depth tiers for a water creature. Water itself is the only thing a swimmer or water
    /// creature passes; props, structures and landmarks in or beside it still stop them.
    /// </summary>
    /// <param name="x">World tile X.</param>
    /// <param name="y">World tile Y.</param>
    /// <param name="includeLandmarks">Whether landmark structures block tiles.</param>
    /// <param name="terrainMask">The mover's collision mask.</param>
    /// <returns><c>true</c> when the tile is open.</returns>
    public bool IsOpen(int x, int y, bool includeLandmarks, uint terrainMask)
    {
        bool waterBound = TerrainMask.IsWaterBound(terrainMask);
        if (!waterBound && this.GetWalkability(x, y, includeLandmarks) != TileWalkability.Impassable)
        {
            return true;
        }

        MapTile tile = _world.PeekTileAtWorld(x, y);
        bool waterOpen = waterBound
            ? TerrainMask.AllowsWaterTier(terrainMask, (int)WaterDepth.TierOf(tile))
            : TerrainMask.CanSwim(terrainMask) && WaterWading.IsWater(tile.Biome);
        return waterOpen &&
            !_world.HasSolidPropAt(x, y) &&
            !_world.Buildings.TryGetStructure(x, y, out _) &&
            !(includeLandmarks && _world.Generator.Landmarks.IsTileBlocked(x, y));
    }

    // Dry river bank close to the channel (width-scaled distance just outside the water threshold).
    private static bool IsRiverside(float riverDistance) => riverDistance is > WorldConstants.RiverChannel and <= 4.0f;

    /// <summary>The grid as a water creature sees it: only its own depth tiers are open (ADR-032 D14).</summary>
    private sealed class WaterBoundGrid(TileWalkabilityGrid grid, uint terrainMask) : ITileWalkabilityGrid
    {
        public TileWalkability GetWalkability(int worldTileX, int worldTileY) =>
            grid.IsOpen(worldTileX, worldTileY, includeLandmarks: true, terrainMask) ? TileWalkability.WalkableNormal : TileWalkability.Impassable;

        public float GetMoveCost(int worldTileX, int worldTileY) =>
            this.IsWalkable(worldTileX, worldTileY) ? 1f : float.PositiveInfinity;

        public bool IsWalkable(int worldTileX, int worldTileY) =>
            grid.IsOpen(worldTileX, worldTileY, includeLandmarks: true, terrainMask);

        public byte GetHeightLevel(int worldTileX, int worldTileY) => grid.GetHeightLevel(worldTileX, worldTileY);

        public bool CanTraverse(int fromX, int fromY, int toX, int toY) =>
            grid.CanTraverse(fromX, fromY, toX, toY, includeLandmarks: true, terrainMask);

        public ITileWalkabilityGrid ForTerrainMask(uint mask) => grid.ForTerrainMask(mask);
    }
}
