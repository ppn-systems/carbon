// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Maps.Biomes;
using Carbon.Maps.Streaming;

namespace Carbon.Creatures.Spawning;

/// <summary>
/// <see cref="IBiomeSampler"/> backed by a procedural <see cref="ChunkWorld2D"/> (allocation-free tile peek).
/// </summary>
public sealed class ChunkWorldBiomeSampler : IBiomeSampler
{
    private readonly ChunkWorld2D _world;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChunkWorldBiomeSampler"/> class.
    /// </summary>
    /// <param name="world">Chunked world map.</param>
    public ChunkWorldBiomeSampler(ChunkWorld2D world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
    }

    /// <inheritdoc/>
    public BiomeType SampleBiome(Vector2 worldPosition)
    {
        int tx = (int)MathF.Floor(worldPosition.X / _world.TileSize);
        int ty = (int)MathF.Floor(worldPosition.Y / _world.TileSize);
        return _world.PeekTileAtWorld(tx, ty).Biome;
    }
}
