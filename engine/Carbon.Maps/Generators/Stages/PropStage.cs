// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Maps.Data;
using Carbon.Maps.Landmarks;
using Carbon.Maps.Noise;
using Carbon.Maps.Props;

namespace Carbon.Maps.Generators.Stages;

/// <summary>
/// Natural props (trees, ores, bushes, shrines) from the biome prop tables, driven by coherent foliage and
/// mineral-vein density fields. Landmark footprints stay clear.
/// </summary>
public sealed class PropStage : IChunkStage
{
    private readonly ulong _seed;
    private readonly FractalNoise2D _foliage;
    private readonly FractalNoise2D _minerals;
    private readonly PointOfInterestResolver _landmarks;

    /// <summary>Initializes a new instance of the <see cref="PropStage"/> class.</summary>
    /// <param name="seed">World seed.</param>
    /// <param name="landmarks">Landmark resolver, whose footprints stay clear of props.</param>
    public PropStage(ulong seed, PointOfInterestResolver landmarks)
    {
        _seed = seed;
        _landmarks = landmarks ?? throw new ArgumentNullException(nameof(landmarks));
        _foliage = new FractalNoise2D(seed ^ 0xFEEDFACECAFEBEEFUL);
        _minerals = new FractalNoise2D(seed ^ 0xDEADBEEF1337CAFEUL);
    }

    /// <inheritdoc/>
    public void Populate(Chunk2D chunk, ChunkContext context)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        ReadOnlySpan<TileSample> tiles = context.Tiles;
        for (int i = 0; i < tiles.Length; i++)
        {
            ref readonly TileSample s = ref tiles[i];
            int lx = i % Chunk2D.Size;
            int ly = i / Chunk2D.Size;

            // Settlement footprints, bridges, and roads stay clear.
            if (chunk.IsRoad(lx, ly) || chunk.IsBridge(lx, ly) || _landmarks.TryGetAt(s.WorldX, s.WorldY, out _))
            {
                continue;
            }

            // 1-tile clearance buffer along roads, bridges, and cliff edges so props never block navigation.
            bool keepClear =
                (lx > 0 && (chunk.IsRoad(lx - 1, ly) || chunk.IsBridge(lx - 1, ly) || tiles[i - 1].Tile.HeightLevel != s.Tile.HeightLevel)) ||
                (lx + 1 < Chunk2D.Size && (chunk.IsRoad(lx + 1, ly) || chunk.IsBridge(lx + 1, ly) || tiles[i + 1].Tile.HeightLevel != s.Tile.HeightLevel)) ||
                (ly > 0 && (chunk.IsRoad(lx, ly - 1) || chunk.IsBridge(lx, ly - 1) || tiles[i - Chunk2D.Size].Tile.HeightLevel != s.Tile.HeightLevel)) ||
                (ly + 1 < Chunk2D.Size && (chunk.IsRoad(lx, ly + 1) || chunk.IsBridge(lx, ly + 1) || tiles[i + Chunk2D.Size].Tile.HeightLevel != s.Tile.HeightLevel));

            if (keepClear)
            {
                continue;
            }

            float foliage = _foliage.Evaluate(s.WorldX * 0.015f, s.WorldY * 0.015f, octaves: 3);
            float minerals = _minerals.Evaluate(s.WorldX * 0.008f, s.WorldY * 0.008f, octaves: 2);
            byte zoneTier = WorldProgression.GetZoneTier(_seed, s.WorldX, s.WorldY);
            if (BiomePropResolver.TryResolveProp(
                s.WorldX,
                s.WorldY,
                lx,
                ly,
                s.Tile.Biome,
                s.Tile.Elevation,
                foliage,
                minerals,
                s.IsNearWater,
                context.TileSize,
                _seed,
                keepClear: false,
                zoneTier,
                out MapProp prop))
            {
                chunk.AddProp(prop);
            }
        }
    }
}
