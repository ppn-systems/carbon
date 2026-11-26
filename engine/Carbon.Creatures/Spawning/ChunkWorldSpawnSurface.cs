// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Streaming;

namespace Carbon.Creatures.Spawning;

/// <summary>
/// <see cref="ISpawnSurface"/> backed by a procedural <see cref="ChunkWorld2D"/>: rejects solid tiles, water and solid
/// props using allocation-free tile peeks.
/// </summary>
public sealed class ChunkWorldSpawnSurface : ISpawnSurface
{
    private readonly ChunkWorld2D _world;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChunkWorldSpawnSurface"/> class.
    /// </summary>
    /// <param name="world">Chunked world map.</param>
    public ChunkWorldSpawnSurface(ChunkWorld2D world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
    }

    /// <inheritdoc/>
    public bool TrySampleSpawnBiome(Vector2 worldPosition, out BiomeType biome)
    {
        int tx = (int)MathF.Floor(worldPosition.X / _world.TileSize);
        int ty = (int)MathF.Floor(worldPosition.Y / _world.TileSize);
        MapTile tile = _world.PeekTileAtWorld(tx, ty);
        biome = tile.Biome;
        return !tile.IsSolid &&
               tile.Biome is not (BiomeType.DeepOcean or BiomeType.ShallowWater) &&
               !_world.HasSolidPropAt(tx, ty);
    }
}
