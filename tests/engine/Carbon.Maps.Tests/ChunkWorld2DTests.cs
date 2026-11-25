// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Props;
using Carbon.Maps.Streaming;
using Carbon.Physics.Obstacles;
using Xunit;

namespace Carbon.Maps.Tests;

public sealed class ChunkWorld2DTests
{
    [Fact]
    public void ChunkWorld2D_InitializesSafeNaturalSpawn()
    {
        ProceduralWorldGenerator generator = new(0x1337BEEF12345678UL);
        ChunkWorld2D world = new(generator, tileSize: 32f);

        Assert.True(world.SpawnWorldPosition.X != 0f || world.SpawnWorldPosition.Y != 0f);

        (int sx, int sy) = world.SpawnTile;
        MapTile spawnTile = world.GetTileAtWorld(sx, sy);
        Assert.False(spawnTile.IsSolid);
    }

    [Fact]
    public void ChunkWorld2D_StreamsChunksAroundPosition()
    {
        ProceduralWorldGenerator generator = new(42UL);
        ChunkWorld2D world = new(generator, tileSize: 32f);

        Vector2 playerPos = world.SpawnWorldPosition;
        world.UpdateAroundPosition(playerPos, chunkRadius: 2, evictionRadius: 5);

        // 2 radius -> (2*2 + 1)^2 = 5^2 = 25 chunks
        Assert.True(world.LoadedChunkCount >= 25);
    }

    [Fact]
    public void ChunkWorld2D_EvictsDistantChunksAndPoolsThem()
    {
        ProceduralWorldGenerator generator = new(999UL);
        ChunkWorld2D world = new(generator, tileSize: 32f);

        // Load at (0, 0)
        world.UpdateAroundPosition(Vector2.Zero, chunkRadius: 2, evictionRadius: 4);
        int initialCount = world.LoadedChunkCount;
        Assert.True(initialCount >= 25);

        // Teleport far away to (100000, 100000)
        Vector2 farPos = new(100000f, 100000f);
        world.UpdateAroundPosition(farPos, chunkRadius: 2, evictionRadius: 4);

        // Old chunks at (0, 0) must be evicted and pushed into the pool
        Assert.True(world.LoadedChunkCount <= 35);
        Assert.True(world.PooledChunkCount > 0);
    }

    [Fact]
    public void Chunk2D_BitShiftCoordinateHelpers()
    {
        int tileX = 65;
        int chunkX = Chunk2D.ToChunkCoord(tileX);
        int localX = Chunk2D.ToLocalCoord(tileX);

        Assert.Equal(2, chunkX);
        Assert.Equal(1, localX);

        int negTile = -1;
        Assert.Equal(-1, Chunk2D.ToChunkCoord(negTile));
        Assert.Equal(31, Chunk2D.ToLocalCoord(negTile));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, -1)]
    [InlineData(1234, -5678)]
    [InlineData(int.MinValue / 2, int.MaxValue / 2)]
    public void Chunk2D_PackKey_And_UnpackKey_RoundTripAccurately(int cx, int cy)
    {
        long key = Chunk2D.PackKey(cx, cy);
        (int rx, int ry) = Chunk2D.UnpackKey(key);

        Assert.Equal(cx, rx);
        Assert.Equal(cy, ry);
    }

    [Fact]
    public void ChunkObstacleGrid_IncorporatesPhysicalProps()
    {
        ProceduralWorldGenerator generator = new(777UL);
        Chunk2D chunk = generator.GenerateChunk(chunkX: 0, chunkY: 0);

        TileObstacleGrid grid = chunk.ToObstacleGrid(tileSize: 32f);

        for (int i = 0; i < chunk.Props.Count; i++)
        {
            MapProp prop = chunk.Props[i];
            if (prop.HasCollision)
            {
                Assert.True(grid.IsSolid(prop.LocalTileX, prop.LocalTileY));
            }
        }
    }
}
