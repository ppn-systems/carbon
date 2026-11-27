// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Hydrology;
using Carbon.Maps.Navigation;
using Carbon.Maps.Streaming;
using Xunit;

namespace Carbon.Maps.Tests;

// ADR-032 D14: a water creature moves and paths only inside the depth tiers it lives in.
public sealed class WaterHabitatTests
{
    private const float Tile = 32f;
    private const int Row = 8;
    private const float Radius = 7f;

    // A painted lake in chunk (0, 0), rows 4..12: land at x <= 5, then one column per tier, then land at x = 13.
    private static readonly (int X, float Depth)[] s_columns =
    [
        (6, 0.010f), // Shore
        (7, 0.030f), // Shallow
        (8, 0.070f), // Medium
        (9, 0.150f), // Deep
        (10, 0.150f),
        (11, 0.250f), // Abyss
        (12, 0.250f),
    ];

    private static readonly uint s_crab = new WaterHabitat(WaterDepthTier.Shore, WaterDepthTier.Medium).ToTerrainMask(uint.MaxValue);
    private static readonly uint s_eel = new WaterHabitat(WaterDepthTier.Deep, WaterDepthTier.Abyss).ToTerrainMask(uint.MaxValue);

    [Fact]
    public void ResolveCircleCollision_ShoreCreature_StopsAtTheLandAndAtTheDeepWater()
    {
        ChunkWorld2D world = CreateLake();

        Vector2 west = Walk(world, Centre(7), -1f, s_crab);
        Vector2 east = Walk(world, Centre(7), 1f, s_crab);

        Assert.True(west.X > 6f * Tile, $"A crab must not climb onto land, but reached x={west.X / Tile:F2} tiles.");
        Assert.True(east.X < 9f * Tile, $"A crab must not enter Deep water, but reached x={east.X / Tile:F2} tiles.");
        Assert.True(east.X > 8.5f * Tile, $"A crab must cross its own Medium water, but stopped at x={east.X / Tile:F2} tiles.");
    }

    [Fact]
    public void ResolveCircleCollision_DeepCreature_CrossesTheDeepButNotTheShallows()
    {
        ChunkWorld2D world = CreateLake();

        Vector2 west = Walk(world, Centre(10), -1f, s_eel);
        Vector2 east = Walk(world, Centre(10), 1f, s_eel);

        Assert.True(west.X > 9f * Tile, $"An eel must not swim into Medium water, but reached x={west.X / Tile:F2} tiles.");
        Assert.True(east.X > 12.5f * Tile && east.X < 13f * Tile, $"An eel must cross the abyss to the far shore, but stopped at x={east.X / Tile:F2} tiles.");
    }

    [Fact]
    public void ResolveCircleCollision_LandWalker_StillWadesAndIsStillStoppedByDeepWater()
    {
        ChunkWorld2D world = CreateLake();

        Vector2 walker = Walk(world, Centre(4), 1f, uint.MaxValue);

        Assert.True(walker.X > 8f * Tile && walker.X < 9f * Tile, $"A walker wades to the deep water edge, but stopped at x={walker.X / Tile:F2} tiles.");
    }

    [Theory]
    [InlineData(4, false, false)]
    [InlineData(6, true, false)]
    [InlineData(7, true, false)]
    [InlineData(8, true, false)]
    [InlineData(9, false, true)]
    [InlineData(11, false, true)]
    [InlineData(13, false, false)]
    public void IsOpen_EachTier_IsOpenOnlyToTheCreaturesLivingThere(int x, bool crab, bool eel)
    {
        ChunkWorld2D world = CreateLake();

        Assert.Equal(crab, world.NavGrid.IsOpen(x, Row, includeLandmarks: true, s_crab));
        Assert.Equal(eel, world.NavGrid.IsOpen(x, Row, includeLandmarks: true, s_eel));
    }

    [Fact]
    public void GetMoveSpeedMultiplier_WaterCreature_IsNotSlowedByItsWater()
    {
        ChunkWorld2D world = CreateLake();

        Assert.Equal(1f, world.GetMoveSpeedMultiplier(Centre(11), s_eel));
        Assert.Equal(1f, world.GetMoveSpeedMultiplier(Centre(7), s_crab));
        Assert.True(world.GetMoveSpeedMultiplier(Centre(7), uint.MaxValue) < 1f, "water still slows a wading walker");
    }

    [Fact]
    public void ForTerrainMask_Pathfinder_RoutesACreatureAroundLandInsideItsWater()
    {
        ChunkWorld2D world = CreateLake();
        Chunk2D chunk = world.GetOrCreateChunk(0, 0);
        chunk.SetTile(7, Row, new MapTile(BiomeType.LushPlains, 0.5f, 0.5f, 0.5f, IsSolid: false, Variant: 0)); // a sand bar
        ITileWalkabilityGrid crabGrid = world.NavGrid.ForTerrainMask(s_crab);
        GridPathfinder pathfinder = new();
        Span<(int X, int Y)> path = stackalloc (int X, int Y)[64];

        PathSearchStatus status = pathfinder.FindPath(crabGrid, (7, Row - 3), (7, Row + 3), path, out int length);

        Assert.Equal(PathSearchStatus.Found, status);
        for (int i = 0; i < length; i++)
        {
            Assert.NotEqual((7, Row), path[i]);
            Assert.True(world.NavGrid.IsOpen(path[i].X, path[i].Y, includeLandmarks: true, s_crab), $"Path tile {path[i]} is outside the crab's water.");
        }

        Assert.Same(world.NavGrid, world.NavGrid.ForTerrainMask(uint.MaxValue));
        Assert.Same(crabGrid, world.NavGrid.ForTerrainMask(s_crab));
    }

    private static Vector2 Walk(ChunkWorld2D world, Vector2 start, float direction, uint mask)
    {
        Vector2 position = start;
        for (int i = 0; i < 400; i++)
        {
            position.X += 1.5f * direction;
            _ = world.ResolveCircleCollision(ref position, Radius, mask, out _);
        }

        return position;
    }

    private static Vector2 Centre(int tx) => new((tx + 0.5f) * Tile, (Row + 0.5f) * Tile);

    private static ChunkWorld2D CreateLake()
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(99UL), tileSize: Tile);
        world.UpdateAroundPosition(Vector2.Zero, chunkRadius: 1, evictionRadius: 3);
        Chunk2D chunk = world.GetOrCreateChunk(0, 0);
        chunk.ClearProps();
        for (int y = 2; y <= 14; y++)
        {
            for (int x = 2; x <= 15; x++)
            {
                chunk.SetTile(x, y, new MapTile(BiomeType.LushPlains, 0.5f, 0.5f, 0.5f, IsSolid: false, Variant: 0));
            }
        }

        for (int y = 4; y <= 12; y++)
        {
            foreach ((int x, float depth) in s_columns)
            {
                BiomeType biome = WaterDepth.BiomeFor(WaterDepth.TierOf(depth));
                chunk.SetTile(x, y, new MapTile(biome, WaterDepth.ToElevation(depth), 0.5f, 0.85f, BiomeProperties.Get(biome).IsSolid, Variant: 0));
            }
        }

        return world;
    }
}
