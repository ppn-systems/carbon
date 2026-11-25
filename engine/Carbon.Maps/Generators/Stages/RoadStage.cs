// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Roads;

namespace Carbon.Maps.Generators.Stages;

/// <summary>
/// Marks the tiles crossed by roads between settlements. Runs before the prop stage, which keeps road
/// tiles clear; roads skip water, cliffs and ground at another height level.
/// </summary>
public sealed class RoadStage : IChunkStage
{
    [ThreadStatic]
    private static List<RoadLink>? s_links;

    private readonly RoadNetwork _roads;

    /// <summary>Initializes a new instance of the <see cref="RoadStage"/> class.</summary>
    /// <param name="roads">The road network.</param>
    public RoadStage(RoadNetwork roads) => _roads = roads ?? throw new ArgumentNullException(nameof(roads));

    /// <inheritdoc/>
    public void Populate(Chunk2D chunk, ChunkContext context)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        List<RoadLink> links = s_links ??= new List<RoadLink>();
        links.Clear();
        int x0 = context.ChunkX * Chunk2D.Size;
        int y0 = context.ChunkY * Chunk2D.Size;
        _roads.Query(x0, y0, x0 + Chunk2D.Size - 1, y0 + Chunk2D.Size - 1, links);
        if (links.Count == 0)
        {
            return;
        }

        ReadOnlySpan<TileSample> tiles = context.Tiles;
        for (int i = 0; i < tiles.Length; i++)
        {
            MapTile tile = tiles[i].Tile;
            if (tile.IsSolid || tile.Biome is BiomeType.ShallowWater or BiomeType.DeepOcean)
            {
                continue;
            }

            int lx = i % Chunk2D.Size;
            int ly = i / Chunk2D.Size;
            foreach (RoadLink link in links)
            {
                if (tile.HeightLevel == link.Level &&
                    RoadNetwork.Distance(link, x0 + lx + 0.5f, y0 + ly + 0.5f) <= RoadNetwork.HalfWidth)
                {
                    chunk.SetRoad(lx, ly);
                    break;
                }
            }
        }
    }
}
