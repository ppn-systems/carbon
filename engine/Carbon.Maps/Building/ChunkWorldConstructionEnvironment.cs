// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Maps.Data;
using Carbon.Maps.Streaming;

namespace Carbon.Maps.Building;

/// <summary>
/// <see cref="IConstructionEnvironment"/> backed by a <see cref="ChunkWorld2D"/>. Terrain reads use
/// <see cref="ChunkWorld2D.PeekTileAtWorld"/> so validation never generates chunks.
/// </summary>
public sealed class ChunkWorldConstructionEnvironment : IConstructionEnvironment
{
    private readonly ChunkWorld2D _world;
    private readonly Func<int, int, bool>? _isEntityAt;

    /// <summary>Initializes a new instance of the <see cref="ChunkWorldConstructionEnvironment"/> class.</summary>
    /// <param name="world">The chunk world.</param>
    /// <param name="isEntityAt">Optional predicate reporting live entities on a tile; none are reported when omitted.</param>
    public ChunkWorldConstructionEnvironment(ChunkWorld2D world, Func<int, int, bool>? isEntityAt = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _isEntityAt = isEntityAt;
    }

    /// <inheritdoc/>
    public MapTile GetTerrain(int worldTileX, int worldTileY) => _world.PeekTileAtWorld(worldTileX, worldTileY);

    /// <inheritdoc/>
    /// <remarks>A stump or sapling also blocks building (ADR-040 D1): dig the stump up to clear the tile.</remarks>
    public bool HasSolidProp(int worldTileX, int worldTileY) =>
        _world.HasSolidPropAt(worldTileX, worldTileY) || _world.HasGrowingTreeAt(worldTileX, worldTileY);

    /// <inheritdoc/>
    public bool IsOccupiedByEntity(int worldTileX, int worldTileY) => _isEntityAt?.Invoke(worldTileX, worldTileY) ?? false;
}
