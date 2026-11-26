// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Hydrology;
using Carbon.Maps.Streaming;
using Xunit;

namespace Carbon.Maps.Tests;

public sealed class WaterWadingTests
{
    private const float Tile = 32f;

    // Test lake carved into chunk (0, 0), rows 4..12: land at x <= 5, shallow water 6..13, deep water 14..15, land 16.
    private const int LandEdgeX = 5;
    private const int DeepStartX = 14;

    [Fact]
    public void DepthFromDistances_AtWaterline_IsZero() =>
        Assert.Equal(0f, WaterWading.DepthFromDistances(0f, float.MaxValue, Tile));

    [Fact]
    public void DepthFromDistances_TwoTilesFromShore_IsFullDepth() =>
        Assert.Equal(1f, WaterWading.DepthFromDistances(2f * Tile, float.MaxValue, Tile));

    [Fact]
    public void DepthFromDistances_AgainstDeepWater_IsFullDepthEvenNearShore() =>
        Assert.Equal(1f, WaterWading.DepthFromDistances(0f, 0f, Tile));

    [Fact]
    public void DepthFromDistances_MovingAwayFromShore_IncreasesMonotonically()
    {
        float previous = -1f;
        for (int i = 0; i <= 16; i++)
        {
            float depth = WaterWading.DepthFromDistances(i * 4f, float.MaxValue, Tile);
            Assert.True(depth > previous, $"depth at {i * 4f} ({depth}) should exceed {previous}");
            previous = depth;
        }
    }

    [Theory]
    [InlineData(0f, 0.75f)]
    [InlineData(1f, 0.40f)]
    [InlineData(0.5f, 0.575f)]
    [InlineData(-3f, 0.75f)]
    [InlineData(7f, 0.40f)]
    public void SpeedMultiplier_MapsDepthLinearlyAndClamps(float depth, float expected) =>
        Assert.Equal(expected, WaterWading.SpeedMultiplier(depth), 4);

    [Fact]
    public void GetWaterDepth_OnDryLand_IsZero()
    {
        ChunkWorld2D world = CreateLakeWorld();

        Assert.Equal(0f, world.GetWaterDepth(Centre(LandEdgeX, 8)));
        Assert.Equal(1.0f, world.GetMoveSpeedMultiplier(Centre(LandEdgeX, 8)));
    }

    [Fact]
    public void GetMoveSpeedMultiplier_ShallowWater_IsSlowerThanLandAndSlowestMidWater()
    {
        ChunkWorld2D world = CreateLakeWorld();

        float waterline = world.GetMoveSpeedMultiplier(new Vector2(((LandEdgeX + 1) * Tile) + 1f, 8.5f * Tile));
        float midWater = world.GetMoveSpeedMultiplier(Centre(9, 8));

        Assert.InRange(waterline, 0.70f, WaterWading.ShoreSpeedMultiplier);
        Assert.Equal(WaterWading.DeepSpeedMultiplier, midWater, 4);
    }

    [Fact]
    public void GetWaterDepth_WalkingFromShoreToDeepWater_NeverDecreases()
    {
        ChunkWorld2D world = CreateLakeWorld();

        float previous = 0f;
        for (float x = (LandEdgeX + 1) * Tile; x < DeepStartX * Tile; x += 2f)
        {
            float depth = world.GetWaterDepth(new Vector2(x, 8.5f * Tile));
            Assert.True(depth >= previous - 0.0001f, $"depth dropped at x={x}: {previous} -> {depth}");
            Assert.InRange(depth, 0f, 1f);
            previous = depth;
        }

        Assert.Equal(1f, previous, 3);
    }

    [Fact]
    public void GetWaterDepth_NextToDeepWater_IsFullDepth()
    {
        ChunkWorld2D world = CreateLakeWorld();

        Assert.Equal(1f, world.GetWaterDepth(new Vector2((DeepStartX * Tile) - 1f, 8.5f * Tile)), 3);
    }

    [Fact]
    public void DeepWater_StaysImpassable()
    {
        ChunkWorld2D world = CreateLakeWorld();

        Assert.True(world.PeekTileAtWorld(DeepStartX, 8).IsSolid);
        Assert.Equal(0f, world.GetMoveSpeedMultiplier(Centre(DeepStartX, 8)));
        Assert.Equal(0f, world.GetWaterDepth(Centre(DeepStartX, 8)));
    }

    [Fact]
    public void GetMoveSpeedMultiplier_SameWorldAndPosition_IsDeterministic()
    {
        ChunkWorld2D a = CreateLakeWorld();
        ChunkWorld2D b = CreateLakeWorld();
        Vector2 p = new(7.3f * Tile, 8.1f * Tile);

        Assert.Equal(a.GetMoveSpeedMultiplier(p), b.GetMoveSpeedMultiplier(p));
    }

    private static Vector2 Centre(int tx, int ty) => new((tx + 0.5f) * Tile, (ty + 0.5f) * Tile);

    private static ChunkWorld2D CreateLakeWorld()
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(99UL), tileSize: Tile);
        world.UpdateAroundPosition(Vector2.Zero, chunkRadius: 1, evictionRadius: 3);
        Chunk2D chunk = world.GetOrCreateChunk(0, 0);

        for (int y = 2; y <= 14; y++)
        {
            for (int x = 2; x <= 18; x++)
            {
                bool lakeRow = y is >= 4 and <= 12;
                BiomeType biome = !lakeRow || x <= LandEdgeX || x >= 16
                    ? BiomeType.LushPlains
                    : x >= DeepStartX ? BiomeType.DeepOcean : BiomeType.ShallowWater;
                chunk.SetTile(x, y, new MapTile(biome, 0.3f, 0.5f, 0.5f, IsSolid: biome == BiomeType.DeepOcean, Variant: 0));
                Assert.False(world.IsRoadAtWorld(x, y) || world.IsBridgeAtWorld(x, y), $"test lake overlaps a road at ({x}, {y})");
            }
        }

        return world;
    }
}
