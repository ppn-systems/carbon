// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Maps.Data;
using Carbon.Maps.Hydrology;
using Carbon.Maps.Streaming;

namespace Carbon.Creatures.Spawning;

/// <summary><see cref="ILairSource"/> over a procedural <see cref="ChunkWorld2D"/>: the <see cref="AbyssLairs"/> of its seed.</summary>
public sealed class ChunkWorldLairSource : ILairSource
{
    private readonly AbyssLairs _lairs;
    private readonly float _tileSize;

    /// <summary>Initializes a new instance of the <see cref="ChunkWorldLairSource"/> class.</summary>
    /// <param name="world">The world.</param>
    /// <param name="lairChance">Chance that a lair cell with open abyss holds a lair.</param>
    public ChunkWorldLairSource(ChunkWorld2D world, float lairChance)
    {
        ArgumentNullException.ThrowIfNull(world);
        _lairs = new AbyssLairs(world.Generator, world.Generator.Seed, lairChance);
        _tileSize = world.TileSize;
    }

    /// <inheritdoc/>
    public float CellSize => AbyssLairs.CellTiles * _tileSize;

    /// <inheritdoc/>
    public float ChunkSize => Chunk2D.Size * _tileSize;

    /// <inheritdoc/>
    public bool TryGetLair(int cellX, int cellY, out Vector2 anchor)
    {
        bool found = _lairs.TryGetLair(cellX, cellY, out (int X, int Y) tile);
        anchor = found ? new Vector2((tile.X + 0.5f) * _tileSize, (tile.Y + 0.5f) * _tileSize) : default;
        return found;
    }
}
