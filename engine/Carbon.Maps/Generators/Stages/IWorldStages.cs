// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Maps.Data;

namespace Carbon.Maps.Generators.Stages;

/// <summary>
/// One step of per-tile terrain synthesis (elevation, climate, hydrology, biome...). Stages run in a
/// fixed order and must be pure functions of the seed and the coordinate, so generation stays deterministic.
/// </summary>
public interface ITileStage
{
    /// <summary>Advances the sample.</summary>
    /// <param name="sample">The tile being synthesised.</param>
    /// <returns><c>false</c> when the stage finalised <see cref="TileSample.Tile"/> and later stages must be skipped.</returns>
    bool Apply(ref TileSample sample);
}

/// <summary>What a chunk stage gets: the chunk and the samples of its tiles in row-major order.</summary>
/// <param name="ChunkX">Chunk X.</param>
/// <param name="ChunkY">Chunk Y.</param>
/// <param name="TileSize">World units per tile.</param>
/// <param name="Tiles">The tile samples, <c>Tiles[(localY * Chunk2D.Size) + localX]</c>.</param>
public readonly ref struct ChunkContext(int ChunkX, int ChunkY, float TileSize, ReadOnlySpan<TileSample> Tiles)
{
    /// <summary>Gets the chunk X.</summary>
    public int ChunkX { get; } = ChunkX;

    /// <summary>Gets the chunk Y.</summary>
    public int ChunkY { get; } = ChunkY;

    /// <summary>Gets the world units per tile.</summary>
    public float TileSize { get; } = TileSize;

    /// <summary>Gets the tile samples in row-major order.</summary>
    public ReadOnlySpan<TileSample> Tiles { get; } = Tiles;
}

/// <summary>
/// A step that decorates a whole chunk once its terrain is known: landmarks, props, and future layers such
/// as caves, roads or ore bodies. Stages run in registration order.
/// </summary>
public interface IChunkStage
{
    /// <summary>Adds this stage's content to the chunk.</summary>
    /// <param name="chunk">The chunk, terrain already set.</param>
    /// <param name="context">The chunk's coordinates and tile samples.</param>
    void Populate(Chunk2D chunk, ChunkContext context);
}
