// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Hydrology;
using Carbon.Maps.Streaming;
using Carbon.Physics.Obstacles;
using Xunit;

namespace Carbon.Maps.Tests;

// ADR-032 D10: depth tiers set the swim state and the speed (water.json), and deep water is open to swimmers only.
public sealed class SwimmingTests
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

    [Fact]
    public void GetSwimStateAt_EachTier_WalksWadesSwimsOrDives()
    {
        ChunkWorld2D world = CreateLake();

        Assert.Equal(SwimState.Walking, world.GetSwimStateAt(Centre(4)));
        Assert.Equal(SwimState.Wading, world.GetSwimStateAt(Centre(6)));
        Assert.Equal(SwimState.Wading, world.GetSwimStateAt(Centre(7)));
        Assert.Equal(SwimState.Swimming, world.GetSwimStateAt(Centre(8)));
        Assert.Equal(SwimState.Diving, world.GetSwimStateAt(Centre(9)));
        Assert.Equal(WaterDepthTier.Abyss, world.GetWaterTierAt(Centre(11)));
        Assert.Equal(SwimState.Diving, world.GetSwimStateAt(Centre(11)));
    }

    [Fact]
    public void GetMoveSpeedMultiplier_TileCentres_FollowTheWaterRulesForTheirDepth()
    {
        ChunkWorld2D world = CreateLake();
        WaterRules rules = world.WaterRules;

        float previous = world.GetMoveSpeedMultiplier(Centre(4));
        foreach ((int x, float depth) in s_columns)
        {
            float speed = world.GetMoveSpeedMultiplier(Centre(x));
            Assert.Equal(rules.SpeedAtDepth(depth), speed, 4);
            Assert.True(speed <= previous, $"Deeper water at x={x} must not be faster ({speed} after {previous}).");
            previous = speed;
        }

        Assert.Equal(rules.AbyssSpeed, world.GetMoveSpeedMultiplier(Centre(11)), 4);
    }

    [Fact]
    public void GetMoveSpeedMultiplier_ConfiguredRules_ChangeTheSwimmingSpeed()
    {
        ChunkWorld2D world = CreateLake();
        float shipped = world.GetMoveSpeedMultiplier(Centre(9));

        world.WaterRules = WaterRules.Default with { DeepSpeed = 0.9f, AbyssSpeed = 0.9f, MediumSpeed = 0.9f };

        Assert.NotEqual(shipped, world.GetMoveSpeedMultiplier(Centre(9)));
        Assert.Equal(0.9f, world.GetMoveSpeedMultiplier(Centre(9)), 4);
    }

    [Fact]
    public void GetMoveSpeedMultiplier_CrossingATileBorder_ChangesSmoothly()
    {
        ChunkWorld2D world = CreateLake();

        float before = world.GetMoveSpeedMultiplier(new Vector2((9f * Tile) - 0.5f, (Row + 0.5f) * Tile));
        float after = world.GetMoveSpeedMultiplier(new Vector2((9f * Tile) + 0.5f, (Row + 0.5f) * Tile));

        Assert.True(System.MathF.Abs(after - before) < 0.01f, $"Speed steps from {before} to {after} at the medium/deep border.");
    }

    [Fact]
    public void ResolveCircleCollision_WalkingIntoDeepWater_StopsWalkersButNotSwimmers()
    {
        ChunkWorld2D world = CreateLake();

        Vector2 walker = WalkEast(world, uint.MaxValue);
        Vector2 swimmer = WalkEast(world, uint.MaxValue & ~TerrainMask.DeepWater);

        Assert.True(walker.X < 9f * Tile, $"A walker must stop at the deep water edge, but reached x={walker.X / Tile:F2} tiles.");
        Assert.True(swimmer.X > 11f * Tile, $"A swimmer must dive into deep water, but stopped at x={swimmer.X / Tile:F2} tiles.");
    }

    [Fact]
    public void CanTraverse_IntoDeepWater_IsOpenToSwimmersOnly()
    {
        ChunkWorld2D world = CreateLake();

        Assert.False(world.NavGrid.CanTraverse(8, Row, 9, Row, includeLandmarks: false, canSwim: false));
        Assert.True(world.NavGrid.CanTraverse(8, Row, 9, Row, includeLandmarks: false, canSwim: true));
        Assert.True(world.NavGrid.CanTraverse(9, Row, 10, Row, includeLandmarks: false, canSwim: true));
    }

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(WaterDepth.MinDepth, 0.80f)]
    [InlineData(WaterDepth.MediumFrom, 0.50f)]
    [InlineData(WaterDepth.AbyssFrom, 0.40f)]
    [InlineData(0.30f, 0.40f)]
    public void SpeedAtDepth_ShippedRules_HoldEachTierValueWhereTheTierStarts(float depth, float expected) =>
        Assert.Equal(expected, WaterRules.Default.SpeedAtDepth(depth), 4);

    [Fact]
    public void LoadFromJson_ShippedDocument_MatchesTheDefaults()
    {
        string json = System.IO.File.ReadAllText(System.IO.Path.Combine(FindRepoRoot(), "resources", "config", "water.json"));

        Assert.Equal(WaterRules.Default, WaterRules.LoadFromJson(json));
    }

    [Theory]
    [InlineData("""{ "speed": { "shore": 0.8, "shallow": 0.6, "medium": 0.5, "deep": 0.45, "abyss": 0 }, "oxygen": { "maxSeconds": 30, "drainPerSecond": { "deep": 1, "abyss": 1.5 }, "refillPerSecond": 10, "drowningEffect": "drowning" } }""")]
    [InlineData("""{ "speed": { "shore": 0.8, "shallow": 0.6, "medium": 0.5, "deep": 0.45, "abyss": 0.4 }, "oxygen": { "maxSeconds": 0, "drainPerSecond": { "deep": 1, "abyss": 1.5 }, "refillPerSecond": 10, "drowningEffect": "drowning" } }""")]
    [InlineData("""{ "speed": { "shore": 0.8, "shallow": 0.6, "medium": 0.5, "deep": 0.45, "abyss": 0.4 }, "oxygen": { "maxSeconds": 30, "drainPerSecond": { "deep": 1, "abyss": 1.5 }, "refillPerSecond": 10, "drowningEffect": "" } }""")]
    [InlineData("""{ "speed": { "shore": 0.8 } }""")]
    public void LoadFromJson_InvalidDocument_IsRejected(string json) =>
        Assert.Throws<System.IO.InvalidDataException>(() => WaterRules.LoadFromJson(json));

    private static Vector2 WalkEast(ChunkWorld2D world, uint mask)
    {
        Vector2 position = new(4.5f * Tile, (Row + 0.5f) * Tile);
        for (int i = 0; i < 400; i++)
        {
            position.X += 1.5f;
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

    private static string FindRepoRoot()
    {
        System.IO.DirectoryInfo? dir = new(System.AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "Carbon.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new System.InvalidOperationException("Repository root not found.");
    }
}
