// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Maps.Data;

namespace Carbon.Maps.Generators.Stages;

/// <summary>
/// Keeps a clearing around the world spawn: no trees, ores or rocks block new players on arrival.
/// </summary>
public sealed class SpawnClearingStage : IChunkStage
{
    /// <summary>Clearing radius in tiles.</summary>
    public const int Radius = 9;

    private readonly Lazy<(int X, int Y)> _spawn;

    /// <summary>Initializes a new instance of the <see cref="SpawnClearingStage"/> class.</summary>
    /// <param name="spawn">The spawn tile, computed lazily once.</param>
    public SpawnClearingStage(Lazy<(int X, int Y)> spawn) => _spawn = spawn ?? throw new ArgumentNullException(nameof(spawn));

    /// <inheritdoc/>
    public void Populate(Chunk2D chunk, ChunkContext context)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        int x0 = context.ChunkX * Chunk2D.Size;
        int y0 = context.ChunkY * Chunk2D.Size;

        // Cheap reject: only the few chunks near the origin can hold the spawn (it is searched outward from 0,0).
        if (Math.Max(Math.Abs(x0), Math.Abs(y0)) > 600 || chunk.Props.Count == 0)
        {
            return;
        }

        (int sx, int sy) = _spawn.Value;
        if (sx + Radius < x0 || sx - Radius >= x0 + Chunk2D.Size || sy + Radius < y0 || sy - Radius >= y0 + Chunk2D.Size)
        {
            return;
        }

        _ = chunk.RemoveProps(p =>
        {
            int dx = x0 + p.LocalTileX - sx;
            int dy = y0 + p.LocalTileY - sy;
            return (dx * dx) + (dy * dy) <= Radius * Radius;
        });
    }
}
