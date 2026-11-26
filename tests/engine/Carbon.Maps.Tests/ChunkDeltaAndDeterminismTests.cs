// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Props;
using Carbon.Maps.Streaming;
using Carbon.Primitives.Hashing;
using Carbon.Primitives.Math;
using Xunit;

namespace Carbon.Maps.Tests;

public sealed class ChunkDeltaAndDeterminismTests
{
    private static readonly Vector2 s_farAway = new(500000f, 500000f);

    [Fact]
    public void SetTileAtWorld_EditedTile_SurvivesChunkEviction()
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(42UL));
        MapTile edited = new(BiomeType.VolcanicAshland, 0.5f, 0.5f, 0.5f, IsSolid: true, Variant: 0);

        world.SetTileAtWorld(10, 12, edited);
        world.UpdateAroundPosition(s_farAway, chunkRadius: 0, evictionRadius: 0);
        Assert.False(world.TryGetChunk(0, 0, out _));

        Assert.Equal(edited, world.PeekTileAtWorld(10, 12));
        Assert.Equal(edited, world.GetTileAtWorld(10, 12));
        Assert.True(world.TryGetDelta(0, 0, out ChunkDelta? delta));
        Assert.Single(delta!.Tiles);
    }

    [Fact]
    public void TryDamagePropAt_DestroyedProp_StaysDestroyedAfterReload()
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(7UL));
        (int tx, int ty) = FindSolidProp(world);

        Assert.True(world.TryDamagePropAt(tx, ty, 1_000_000f, out MapProp destroyed));
        Assert.True(destroyed.Destructibility.IsDestroyed);
        Assert.False(world.HasSolidPropAt(tx, ty));

        world.UpdateAroundPosition(s_farAway, chunkRadius: 0, evictionRadius: 0);
        _ = world.GetOrCreateChunk(Chunk2D.ToChunkCoord(tx), Chunk2D.ToChunkCoord(ty));

        Assert.False(world.HasSolidPropAt(tx, ty));
    }

    [Fact]
    public void ImportDelta_ResidentChunk_AppliesImmediately()
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(42UL));
        _ = world.GetOrCreateChunk(0, 0);
        MapTile edited = new(BiomeType.RedMesa, 0.6f, 0.6f, 0.2f, IsSolid: false, Variant: 1);
        ChunkDelta delta = new(0, 0);
        delta.SetTile(3, 4, edited);

        world.ImportDelta(delta);

        Assert.Equal(edited, world.GetTileAtWorld(3, 4));
    }

    [Fact]
    public void PeekTileAtWorld_UnloadedChunk_DoesNotGenerateChunk()
    {
        ProceduralWorldGenerator generator = new(42UL);
        ChunkWorld2D world = new(generator);

        MapTile peeked = world.PeekTileAtWorld(5000, -7000);

        Assert.Equal(0, world.LoadedChunkCount);
        Assert.Equal(generator.SampleTile(5000, -7000), peeked);
    }

    [Fact]
    public void ResolveEntityCollision_UnloadedArea_DoesNotGenerateChunks()
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(42UL));
        Vector2 position = world.SpawnWorldPosition;

        _ = world.ResolveEntityCollision(ref position, radius: 8f, out _);

        Assert.Equal(0, world.LoadedChunkCount);
    }

    [Fact]
    public void GetOrCreateChunk_ConcurrentCallers_ShareOneInstance()
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(42UL));
        Chunk2D[] results = new Chunk2D[16];

        Parallel.For(0, results.Length, i => results[i] = world.GetOrCreateChunk(3, -2));

        for (int i = 1; i < results.Length; i++)
        {
            Assert.Same(results[0], results[i]);
        }

        Assert.Equal(1, world.LoadedChunkCount);
    }

    [Fact]
    public void GetRiverDistance_DifferentSeeds_ProduceDifferentRivers()
    {
        ProceduralWorldGenerator a = new(1UL);
        ProceduralWorldGenerator b = new(2UL);
        int waterA = 0;
        int differing = 0;
        for (int y = -1024; y < 1024; y += 4)
        {
            for (int x = -1024; x < 1024; x += 4)
            {
                bool wa = a.GetRiverDistance(x, y) <= 2.2f;
                bool wb = b.GetRiverDistance(x, y) <= 2.2f;
                waterA += wa ? 1 : 0;
                differing += wa != wb ? 1 : 0;
            }
        }

        Assert.True(waterA > 100, "Seed 1 should have rivers in a 2048x2048 area.");
        Assert.True(differing > waterA / 2, "Different seeds must not share river courses.");
    }

    [Fact]
    public void GetRiverDistance_FarFromOrigin_StillFindsRivers()
    {
        ProceduralWorldGenerator generator = new(42UL);
        float nearest = float.MaxValue;
        for (int y = 1_000_000; y < 1_002_000; y++)
        {
            nearest = MathF.Min(nearest, generator.GetRiverDistance(2_000_000, y));
        }

        Assert.True(nearest < 1f);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.5f)]
    [InlineData(-2.9f)]
    [InlineData(123.456f)]
    [InlineData(-9876.5f)]
    public void DeterministicMath_Sin_MatchesMathF(float x)
    {
        Assert.Equal(MathF.Sin(x), DeterministicMath.Sin(x), 0.0005f);
        Assert.Equal(MathF.Cos(x), DeterministicMath.Cos(x), 0.0005f);
    }

    [Fact]
    public void SpatialHash_AdjacentTiles_AreDecorrelated()
    {
        HashSet<ulong> seen = new();
        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 32; x++)
            {
                Assert.True(seen.Add(SpatialHash.Hash(42UL, x, y)));
            }
        }

        // Low bits must be balanced, unlike the old (x * p1) ^ (y * p2) hash.
        int even = 0;
        foreach (ulong h in seen)
        {
            even += (h & 1) == 0 ? 1 : 0;
        }

        Assert.InRange(even, 412, 612);
    }

    [Theory]
    [InlineData(42UL)]
    [InlineData(7UL)]
    public void SampleTile_HeightLevels_FormNoTinyIsolatedRegions(ulong seed)
    {
        // Height levels come from the smooth relief, so plateaus/mountains are broad: no region of a
        // level may be smaller than 16 tiles (one-tile pillars and pits are unwalkable noise).
        const int N = 384;
        ProceduralWorldGenerator generator = new(seed);
        byte[] h = new byte[N * N];
        for (int y = 0; y < N; y++)
        {
            for (int x = 0; x < N; x++)
            {
                h[(y * N) + x] = generator.SampleTile(x - (N / 2), y - (N / 2)).HeightLevel;
            }
        }

        bool[] seen = new bool[N * N];
        Stack<int> stack = new();
        for (int i = 0; i < h.Length; i++)
        {
            if (seen[i])
            {
                continue;
            }

            int size = 0;
            bool touchesBorder = false;
            seen[i] = true;
            stack.Push(i);
            while (stack.Count > 0)
            {
                int k = stack.Pop();
                size++;
                int kx = k % N;
                int ky = k / N;
                touchesBorder |= kx == 0 || ky == 0 || kx == N - 1 || ky == N - 1;
                foreach ((int dx, int dy) in s_neighbours)
                {
                    int nx = kx + dx;
                    int ny = ky + dy;
                    int j = (ny * N) + nx;
                    if (nx >= 0 && ny >= 0 && nx < N && ny < N && !seen[j] && h[j] == h[i])
                    {
                        seen[j] = true;
                        stack.Push(j);
                    }
                }
            }

            Assert.True(touchesBorder || size >= 16, $"Isolated height-level region of {size} tiles at index {i}.");
        }
    }

    private static readonly (int, int)[] s_neighbours = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    [Fact]
    public void GenerateChunk_SameSeed_IsBitIdenticalAcrossRuns()
    {
        Chunk2D first = new ProceduralWorldGenerator(42UL).GenerateChunk(3, -2);
        Chunk2D second = new ProceduralWorldGenerator(42UL).GenerateChunk(3, -2);

        Assert.Equal(HashChunk(first), HashChunk(second));
    }

    [Fact]
    public void GenerateChunk_GoldenHash_IsStable()
    {
        // Pins the generator output. If this fails after an intentional generation change, update the
        // constant AND bump the world-generation version so clients regenerate their caches.
        Chunk2D chunk = new ProceduralWorldGenerator(42UL).GenerateChunk(3, -2);

        Assert.Equal(GoldenChunkHash, HashChunk(chunk));
    }

    private const ulong GoldenChunkHash = 12284470238363733661UL;

    private static ulong HashChunk(Chunk2D chunk)
    {
        ulong h = 1469598103934665603UL;
        for (int y = 0; y < Chunk2D.Size; y++)
        {
            for (int x = 0; x < Chunk2D.Size; x++)
            {
                MapTile t = chunk.GetTile(x, y);
                h = (h ^ (ulong)t.Biome) * 1099511628211UL;
                h = (h ^ BitConverter.SingleToUInt32Bits(t.Elevation)) * 1099511628211UL;
                h = (h ^ (t.IsSolid ? 1UL : 0UL)) * 1099511628211UL;
                h = (h ^ t.Variant) * 1099511628211UL;
                h = (h ^ t.HeightLevel) * 1099511628211UL;
            }
        }

        for (int i = 0; i < chunk.Props.Count; i++)
        {
            h = (h ^ (ulong)chunk.Props[i].Type) * 1099511628211UL;
            h = (h ^ BitConverter.SingleToUInt32Bits(chunk.Props[i].WorldPositionX)) * 1099511628211UL;
        }

        return h;
    }

    private static (int X, int Y) FindSolidProp(ChunkWorld2D world)
    {
        for (int cy = -4; cy <= 4; cy++)
        {
            for (int cx = -4; cx <= 4; cx++)
            {
                Chunk2D chunk = world.GetOrCreateChunk(cx, cy);
                for (int i = 0; i < chunk.Props.Count; i++)
                {
                    MapProp p = chunk.Props[i];
                    if (p.HasCollision && p.Destructibility.IsDestructible)
                    {
                        return ((cx * Chunk2D.Size) + p.LocalTileX, (cy * Chunk2D.Size) + p.LocalTileY);
                    }
                }
            }
        }

        throw new InvalidOperationException("No destructible solid prop found near origin.");
    }
}
