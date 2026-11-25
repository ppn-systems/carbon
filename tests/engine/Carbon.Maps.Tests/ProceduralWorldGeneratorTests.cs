// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Physics.Obstacles;
using Xunit;

namespace Carbon.Maps.Tests;

public sealed class ProceduralWorldGeneratorTests
{
    [Fact]
    public void GenerateTilemap_DeterministicSeed_ProducesIdenticalMaps()
    {
        ulong seed = 0x1234567890ABCDEFUL;
        ProceduralWorldGenerator gen1 = new(seed);
        ProceduralWorldGenerator gen2 = new(seed);

        TilemapData map1 = gen1.GenerateTilemap(32, 32);
        TilemapData map2 = gen2.GenerateTilemap(32, 32);

        Assert.Equal(map1.TotalTiles, map2.TotalTiles);

        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 32; x++)
            {
                ref readonly MapTile t1 = ref map1.GetTile(x, y);
                ref readonly MapTile t2 = ref map2.GetTile(x, y);

                Assert.Equal(t1.Biome, t2.Biome);
                Assert.Equal(t1.Elevation, t2.Elevation);
                Assert.Equal(t1.IsSolid, t2.IsSolid);
                Assert.Equal(t1.ColorRgba, t2.ColorRgba);
            }
        }
    }

    [Fact]
    public void GenerateTilemap_DifferentSeed_ProducesDifferentMaps()
    {
        ProceduralWorldGenerator gen1 = new(1111UL);
        ProceduralWorldGenerator gen2 = new(9999UL);

        TilemapData map1 = gen1.GenerateTilemap(32, 32);
        TilemapData map2 = gen2.GenerateTilemap(32, 32);

        bool foundDifference = false;
        for (int i = 0; i < 32 && !foundDifference; i++)
        {
            if (map1.GetTile(i, i).Biome != map2.GetTile(i, i).Biome)
            {
                foundDifference = true;
            }
        }

        Assert.True(foundDifference);
    }

    [Fact]
    public void GenerateTilemap_ToObstacleGrid_AccuratelyPopulatesSolidTiles()
    {
        ProceduralWorldGenerator generator = new(42UL);
        TilemapData map = generator.GenerateTilemap(20, 20);
        TileObstacleGrid obstacleGrid = map.ToObstacleGrid(tileSize: 32f);

        for (int y = 0; y < 20; y++)
        {
            for (int x = 0; x < 20; x++)
            {
                bool expectedSolid = map.GetTile(x, y).IsSolid;
                Assert.Equal(expectedSolid, obstacleGrid.IsSolid(x, y));
            }
        }
    }

    [Fact]
    public void FindSpawnPoint_LocatesWalkableLand()
    {
        ProceduralWorldGenerator generator = new(1337UL);
        TilemapData map = generator.GenerateTilemap(50, 50);

        Vector2 spawn = map.FindSpawnPoint(tileSize: 32f);

        // Convert spawn point back to tile
        int tx = (int)(spawn.X / 32f);
        int ty = (int)(spawn.Y / 32f);

        ref readonly MapTile tile = ref map.GetTile(tx, ty);
        Assert.False(tile.IsSolid);
    }

    [Fact]
    public void GenerateChunk_SeamlessAcrossBoundaries()
    {
        ulong seed = 777UL;
        ProceduralWorldGenerator generator = new(seed);

        Chunk2D chunkA = generator.GenerateChunk(chunkX: 0, chunkY: 0);
        Chunk2D chunkB = generator.GenerateChunk(chunkX: 1, chunkY: 0);

        // Right edge of chunk A is X=31, left edge of chunk B is X=0 (world X=32)
        ref readonly MapTile tileA = ref chunkA.GetTile(31, 15);
        ref readonly MapTile tileB = ref chunkB.GetTile(0, 15);

        MapTile sampleA = generator.SampleTile(31, 15);
        MapTile sampleB = generator.SampleTile(32, 15);

        Assert.Equal(sampleA.Biome, tileA.Biome);
        Assert.Equal(sampleB.Biome, tileB.Biome);
    }
}
