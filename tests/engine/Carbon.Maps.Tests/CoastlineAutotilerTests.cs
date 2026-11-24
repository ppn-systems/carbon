// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Streaming;
using Carbon.Maps.Transitions;
using Xunit;

namespace Carbon.Maps.Tests;

public sealed class CoastlineAutotilerTests
{
    [Fact]
    public void GetAdjacencyMask_InternalChunk_ReturnsCorrectMask()
    {
        ProceduralWorldGenerator generator = new(12345UL);
        ChunkWorld2D world = new(generator, tileSize: 32f);
        Chunk2D chunk = new(0, 0);

        // Fill chunk with ShallowWater
        for (int y = 0; y < Chunk2D.Size; y++)
        {
            for (int x = 0; x < Chunk2D.Size; x++)
            {
                chunk.SetTile(x, y, new MapTile(BiomeType.ShallowWater, 0f, 0f, 0f, false, 0));
            }
        }

        // Set North and West to DeepOcean for tile at (10, 10)
        chunk.SetTile(10, 9, new MapTile(BiomeType.DeepOcean, 0f, 0f, 0f, true, 0));
        chunk.SetTile(9, 10, new MapTile(BiomeType.DeepOcean, 0f, 0f, 0f, true, 0));
        chunk.SetTile(9, 9, new MapTile(BiomeType.DeepOcean, 0f, 0f, 0f, true, 0));

        NeighborMask mask = CoastlineAutotiler.GetAdjacencyMask(chunk, 10, 10, 10, 10, world, BiomeType.DeepOcean);

        Assert.Equal(NeighborMask.North, mask & NeighborMask.North);
        Assert.Equal(NeighborMask.West, mask & NeighborMask.West);
        Assert.Equal(NeighborMask.NorthWest, mask & NeighborMask.NorthWest);
        Assert.Equal(NeighborMask.None, mask & NeighborMask.East);
        Assert.Equal(NeighborMask.None, mask & NeighborMask.South);
    }

    [Fact]
    public void GetAdjacencyMask_StaircaseConfiguration_DetectsComplementaryBevels()
    {
        ProceduralWorldGenerator generator = new(4321UL);
        ChunkWorld2D world = new(generator, tileSize: 32f);
        Chunk2D chunk = new(0, 0);

        // Staircase layout:
        // (1, 0) is ShallowWater, South=(1, 1) DeepOcean, East=(2, 0) DeepOcean (convex shallow corner)
        // (1, 1) is DeepOcean, North=(1, 0) ShallowWater, West=(0, 1) ShallowWater (concave ocean corner)
        chunk.SetTile(1, 0, new MapTile(BiomeType.ShallowWater, 0f, 0f, 0f, false, 0));
        chunk.SetTile(0, 1, new MapTile(BiomeType.ShallowWater, 0f, 0f, 0f, false, 0));
        chunk.SetTile(1, 1, new MapTile(BiomeType.DeepOcean, 0f, 0f, 0f, true, 0));
        chunk.SetTile(2, 0, new MapTile(BiomeType.DeepOcean, 0f, 0f, 0f, true, 0));
        chunk.SetTile(2, 1, new MapTile(BiomeType.DeepOcean, 0f, 0f, 0f, true, 0));

        // Evaluate convex corner at (1, 0)
        NeighborMask shallowOceanMask = CoastlineAutotiler.GetAdjacencyMask(chunk, 1, 0, 1, 0, world, BiomeType.DeepOcean);
        Assert.Equal(NeighborMask.South, shallowOceanMask & NeighborMask.South);
        Assert.Equal(NeighborMask.East, shallowOceanMask & NeighborMask.East);

        // Evaluate concave corner at (1, 1)
        NeighborMask oceanShallowMask = CoastlineAutotiler.GetAdjacencyMask(chunk, 1, 1, 1, 1, world, BiomeType.ShallowWater);
        Assert.Equal(NeighborMask.North, oceanShallowMask & NeighborMask.North);
        Assert.Equal(NeighborMask.West, oceanShallowMask & NeighborMask.West);
    }

    [Fact]
    public void GetAdjacencyMask_CrossChunkBoundary_QueriesWorldAccurately()
    {
        ProceduralWorldGenerator generator = new(98765UL);
        ChunkWorld2D world = new(generator, tileSize: 32f);
        world.UpdateAroundPosition(Vector2.Zero, chunkRadius: 1, evictionRadius: 2);

        Chunk2D? chunk = null;
        foreach (Chunk2D active in world.ActiveChunks)
        {
            if (active.ChunkX == 0 && active.ChunkY == 0)
            {
                chunk = active;
                break;
            }
        }

        Assert.NotNull(chunk);

        // lx = 0, ly = 0 -> North is at wy = -1 (outside chunk 0, 0, resident in chunk 0, -1)
        NeighborMask mask = CoastlineAutotiler.GetAdjacencyMask(chunk, 0, 0, 0, 0, world, BiomeType.DeepOcean);

        MapTile northTile = world.GetTileAtWorld(0, -1);
        NeighborMask expectedNorth = northTile.Biome == BiomeType.DeepOcean ? NeighborMask.North : NeighborMask.None;
        Assert.Equal(expectedNorth, mask & NeighborMask.North);
    }

    [Fact]
    public void GetLandAdjacencyMask_IdentifiesLandAndExcludesWater()
    {
        ProceduralWorldGenerator generator = new(55555UL);
        ChunkWorld2D world = new(generator, tileSize: 32f);
        Chunk2D chunk = new(0, 0);

        // Center at (5, 5) is ShallowWater
        chunk.SetTile(5, 5, new MapTile(BiomeType.ShallowWater, 0f, 0f, 0f, false, 0));

        // North is BeachSand (Land), East is LushPlains (Land), South is DeepOcean (Water), West is ShallowWater (Water)
        chunk.SetTile(5, 4, new MapTile(BiomeType.BeachSand, 0f, 0f, 0f, false, 0));
        chunk.SetTile(6, 5, new MapTile(BiomeType.LushPlains, 0f, 0f, 0f, false, 0));
        chunk.SetTile(5, 6, new MapTile(BiomeType.DeepOcean, 0f, 0f, 0f, true, 0));
        chunk.SetTile(4, 5, new MapTile(BiomeType.ShallowWater, 0f, 0f, 0f, false, 0));

        NeighborMask landMask = CoastlineAutotiler.GetLandAdjacencyMask(chunk, 5, 5, 5, 5, world);

        Assert.Equal(NeighborMask.North, landMask & NeighborMask.North);
        Assert.Equal(NeighborMask.East, landMask & NeighborMask.East);
        Assert.Equal(NeighborMask.None, landMask & NeighborMask.South);
        Assert.Equal(NeighborMask.None, landMask & NeighborMask.West);
    }

    [Fact]
    public void GetWaterAdjacencyMask_IdentifiesBothShallowAndDeepWater()
    {
        ProceduralWorldGenerator generator = new(77777UL);
        ChunkWorld2D world = new(generator, tileSize: 32f);
        Chunk2D chunk = new(0, 0);

        // Center at (8, 8) is BeachSand (Land)
        chunk.SetTile(8, 8, new MapTile(BiomeType.BeachSand, 0f, 0f, 0f, false, 0));

        // North is DeepOcean, East is ShallowWater, South is LushPlains (Land), West is AncientForest (Land)
        chunk.SetTile(8, 7, new MapTile(BiomeType.DeepOcean, 0f, 0f, 0f, true, 0));
        chunk.SetTile(9, 8, new MapTile(BiomeType.ShallowWater, 0f, 0f, 0f, false, 0));
        chunk.SetTile(8, 9, new MapTile(BiomeType.LushPlains, 0f, 0f, 0f, false, 0));
        chunk.SetTile(7, 8, new MapTile(BiomeType.AncientForest, 0f, 0f, 0f, false, 0));

        NeighborMask waterMask = CoastlineAutotiler.GetWaterAdjacencyMask(chunk, 8, 8, 8, 8, world);

        Assert.Equal(NeighborMask.North, waterMask & NeighborMask.North);
        Assert.Equal(NeighborMask.East, waterMask & NeighborMask.East);
        Assert.Equal(NeighborMask.None, waterMask & NeighborMask.South);
        Assert.Equal(NeighborMask.None, waterMask & NeighborMask.West);
    }
}
