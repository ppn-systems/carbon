// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Generators.Stages;
using Carbon.Maps.Landmarks;
using Carbon.Maps.Roads;
using Xunit;

namespace Carbon.Maps.Tests;

public sealed class WorldFeatureStageTests
{
    [Fact]
    public void Roads_MarkDryTilesAlongLinks_AndStayClearOfSolidProps()
    {
        ProceduralWorldGenerator generator = new(7UL);
        List<RoadLink> links = new();
        generator.Roads.Query(-4 * 224, -4 * 224, 4 * 224, 4 * 224, links);
        Assert.NotEmpty(links);

        RoadLink link = links[0];
        int roadTiles = 0;
        for (int i = 0; i + 1 < link.Points.Length; i += 8)
        {
            int tx = (int)MathF.Floor(link.Points[i]);
            int ty = (int)MathF.Floor(link.Points[i + 1]);
            Chunk2D chunk = generator.GenerateChunk(Chunk2D.ToChunkCoord(tx), Chunk2D.ToChunkCoord(ty));
            for (int y = 0; y < Chunk2D.Size; y++)
            {
                for (int x = 0; x < Chunk2D.Size; x++)
                {
                    if (!chunk.IsRoad(x, y))
                    {
                        continue;
                    }

                    roadTiles++;
                    MapTile tile = chunk.GetTile(x, y);
                    Assert.False(tile.IsSolid);
                    Assert.NotEqual(BiomeType.ShallowWater, tile.Biome);
                    Assert.Equal(link.Level, tile.HeightLevel);
                    Assert.False(chunk.HasSolidPropAt(x, y), "Roads are kept clear of trees and rocks.");
                }
            }
        }

        Assert.True(roadTiles > 0, "The chunks along a road link contain road tiles.");
    }

    [Fact]
    public void SpawnClearing_LeavesNoPropsAroundSpawn()
    {
        foreach (ulong seed in new[] { 1UL, 42UL, 1337UL })
        {
            ProceduralWorldGenerator generator = new(seed);
            (int sx, int sy) = generator.SpawnTile;
            for (int cy = Chunk2D.ToChunkCoord(sy - SpawnClearingStage.Radius); cy <= Chunk2D.ToChunkCoord(sy + SpawnClearingStage.Radius); cy++)
            {
                for (int cx = Chunk2D.ToChunkCoord(sx - SpawnClearingStage.Radius); cx <= Chunk2D.ToChunkCoord(sx + SpawnClearingStage.Radius); cx++)
                {
                    Chunk2D chunk = generator.GenerateChunk(cx, cy);
                    foreach (Props.MapProp prop in chunk.Props)
                    {
                        int dx = (cx * Chunk2D.Size) + prop.LocalTileX - sx;
                        int dy = (cy * Chunk2D.Size) + prop.LocalTileY - sy;
                        Assert.True((dx * dx) + (dy * dy) > SpawnClearingStage.Radius * SpawnClearingStage.Radius);
                    }
                }
            }
        }
    }

    [Fact]
    public void MineEntrance_SitsOnPlateauRock_WithPortal()
    {
        ProceduralWorldGenerator generator = new(42UL);
        int mines = 0;
        for (int sy = -6; sy < 6; sy++)
        {
            for (int sx = -6; sx < 6; sx++)
            {
                PointOfInterest poi = generator.Landmarks.GetSite(sx, sy);
                if (poi.Type != PointOfInterestType.MineEntrance)
                {
                    continue;
                }

                mines++;
                Assert.Equal(1, generator.SampleTile(poi.TileX, poi.TileY).HeightLevel);
                LandmarkLayout layout = generator.Landmarks.GetLayout(sx, sy);
                Assert.Equal(LandmarkPieceKind.MineEntrance, layout.Pieces[0].Kind);
            }
        }

        Assert.True(mines > 0, "A 12x12 site area of seed 42 holds a mine.");
    }
}
