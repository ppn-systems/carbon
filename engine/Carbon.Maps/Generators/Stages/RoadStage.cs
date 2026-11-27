// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Hydrology;
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
        for (int li = 0; li < links.Count; li++)
        {
            RoadLink link = links[li];
            float reach = link.IsBridge ? RoadNetwork.BridgeHalfWidth + 0.5f : RoadNetwork.HalfWidth + 0.5f;
            int minLx = Math.Max(0, (int)MathF.Floor(link.MinX - reach) - x0);
            int maxLx = Math.Min(Chunk2D.Size - 1, (int)MathF.Ceiling(link.MaxX + reach) - x0);
            int minLy = Math.Max(0, (int)MathF.Floor(link.MinY - reach) - y0);
            int maxLy = Math.Min(Chunk2D.Size - 1, (int)MathF.Ceiling(link.MaxY + reach) - y0);

            for (int ly = minLy; ly <= maxLy; ly++)
            {
                int row = ly * Chunk2D.Size;
                float py = y0 + ly + 0.5f;
                for (int lx = minLx; lx <= maxLx; lx++)
                {
                    MapTile tile = tiles[row + lx].Tile;
                    float dist = RoadNetwork.Distance(link, x0 + lx + 0.5f, py);
                    if (link.IsBridge)
                    {
                        if (dist <= RoadNetwork.BridgeHalfWidth && tile.Biome is BiomeType.ShallowWater or BiomeType.DeepOcean)
                        {
                            chunk.SetBridge(lx, ly);
                        }
                    }
                    else if (!tile.IsSolid && tile.Biome is not (BiomeType.ShallowWater or BiomeType.DeepOcean))
                    {
                        if (tile.HeightLevel == link.Level && dist <= RoadNetwork.HalfWidth)
                        {
                            chunk.SetRoad(lx, ly);
                        }
                    }
                    else if (tile.Biome is BiomeType.ShallowWater or BiomeType.DeepOcean &&
                             (tiles[row + lx].RiverDistance <= WorldConstants.RiverChannel || WaterDepth.Of(tile) < WaterDepth.DeepFrom) &&
                             dist <= RoadNetwork.BridgeHalfWidth)
                    {
                        // Bridge shallow water / river channels clipped along a road polyline.
                        chunk.SetBridge(lx, ly);
                    }
                }
            }
        }
    }
}
