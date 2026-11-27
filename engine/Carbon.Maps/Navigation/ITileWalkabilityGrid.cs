// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Maps.Navigation;

/// <summary>
/// Read-only spatial navigation grid protocol for pathfinding, AI traversal, and movement queries.
/// Adheres to Interface Segregation Principle (ISP) and provides zero-allocation lookups.
/// </summary>
public interface ITileWalkabilityGrid
{
    /// <summary>
    /// Resolves the walkability classification of a tile in world coordinates.
    /// </summary>
    TileWalkability GetWalkability(int worldTileX, int worldTileY);

    /// <summary>
    /// Resolves the pathfinding traversal cost multiplier of a tile in world coordinates.
    /// Impassable tiles return float.PositiveInfinity.
    /// </summary>
    float GetMoveCost(int worldTileX, int worldTileY);

    /// <summary>
    /// Evaluates whether a tile can be traversed by entities and monsters.
    /// </summary>
    bool IsWalkable(int worldTileX, int worldTileY);

    /// <summary>
    /// Resolves the discrete 2.5D height level [0..2] of a world tile.
    /// </summary>
    byte GetHeightLevel(int worldTileX, int worldTileY);

    /// <summary>
    /// Evaluates whether an entity can step from (fromX, fromY) to adjacent (toX, toY),
    /// respecting obstacles, walls, cliffs, ramp alignments, and corner cutting.
    /// </summary>
    bool CanTraverse(int fromX, int fromY, int toX, int toY);

    /// <summary>
    /// Gets this grid as a collider with <paramref name="terrainMask"/> sees it (see
    /// <see cref="Carbon.Physics.Obstacles.TerrainMask"/>): a water creature walks only its own depth tiers
    /// (ADR-032 D14). Grids without water return themselves.
    /// </summary>
    /// <param name="terrainMask">The mover's collision mask.</param>
    /// <returns>The grid for that mover.</returns>
    ITileWalkabilityGrid ForTerrainMask(uint terrainMask) => this;
}
