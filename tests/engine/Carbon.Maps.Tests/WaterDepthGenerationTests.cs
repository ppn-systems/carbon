// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Generators.Stages;
using Carbon.Maps.Hydrology;
using Xunit;

namespace Carbon.Maps.Tests;

// ADR-032: the generated water depth field (tiers, river width vs depth, outlets, the sea shelf).
public sealed class WaterDepthGenerationTests
{
    // One stored depth step (the river network quantizes depth to a byte).
    private const float DepthQuantum = WaterDepth.MaxInlandDepth / 255f;

    // A window with wide trunk rivers, streams and lakes (seed 1337), and one with a coast (seed 42).
    public static TheoryData<ulong, int, int> Windows => new()
    {
        { 1337UL, -556, 44 },
        { 42UL, -256, -256 },
    };

    // Land, rivers and lakes round the origin (seed 1337), and the coast and open sea of the shore transect (seed 42).
    [Theory]
    [InlineData(1337UL, 0, 0)]
    [InlineData(42UL, 31, -188)]
    public void GenerateChunk_SameSeedInFreshGenerators_ProducesIdenticalChunksAndMatchesSingleTileSampling(ulong seed, int centreX, int centreY)
    {
        ProceduralWorldGenerator first = new(seed);
        ProceduralWorldGenerator second = new(seed);

        for (int chunkY = centreY - 2; chunkY <= centreY + 2; chunkY++)
        {
            for (int chunkX = centreX - 2; chunkX <= centreX + 2; chunkX++)
            {
                Chunk2D a = first.GenerateChunk(chunkX, chunkY);
                Chunk2D b = second.GenerateChunk(chunkX, chunkY);
                for (int y = 0; y < Chunk2D.Size; y++)
                {
                    for (int x = 0; x < Chunk2D.Size; x++)
                    {
                        MapTile tile = a.GetTile(x, y);
                        Assert.Equal(tile, b.GetTile(x, y));
                        Assert.Equal(tile, second.SampleTile((chunkX * Chunk2D.Size) + x, (chunkY * Chunk2D.Size) + y));
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Windows))]
    public void Generate_InlandWater_IsNeverDeeperThanItsWidthAllows(ulong seed, int x0, int y0)
    {
        const int Size = 512;
        WaterWindow window = WaterWindow.Sample(seed, x0, y0, Size);
        int checkedTiles = 0;
        int narrowTiles = 0;

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                if (!window.IsInlandWater(x, y) || !window.TryMeasureWidth(x, y, out int width))
                {
                    continue;
                }

                checkedTiles++;
                MapTile tile = window.Tile(x, y);
                float depth = WaterDepth.Of(tile);
                float allowed = WaterDepth.MaxDepthForChannelWidth(width) + DepthQuantum;
                Assert.True(
                    depth <= allowed,
                    $"Inland water at {x0 + x},{y0 + y} is {depth:F4} deep ({WaterDepth.TierOf(depth)}) but only {width} tiles wide (at most {allowed:F4}).");

                if (width < 8)
                {
                    narrowTiles++;
                    Assert.True(
                        tile.Biome == BiomeType.ShallowWater && WaterDepth.TierOf(depth) <= WaterDepthTier.Shallow,
                        $"A {width}-tile stream at {x0 + x},{y0 + y} is {WaterDepth.TierOf(depth)} ({tile.Biome}).");
                }
            }
        }

        Assert.True(checkedTiles > 2000, $"Only {checkedTiles} inland water tiles measured: the window has no rivers.");
        Assert.True(narrowTiles > 100, $"Only {narrowTiles} narrow stream tiles measured.");
    }

    // Floods each body of water from a window, following it past the window edge if it must. A body that reaches the
    // sea drains. Any other body is a river system that must end in a lake: water runs downhill, so its lowest tile
    // must lie in open water clearly wider than the body's usual channel (or the body is all lake: typically at least
    // LakeWidth across). Without the pool, a river that runs out of course ends there in a channel tip no wider
    // than the river itself.
    [Theory]
    [InlineData(1337UL, -556, 44)]
    [InlineData(1337UL, 180, -680)] // a river that runs out of course and ends in its pool at 372,-489
    public void Generate_EveryRiver_DrainsIntoTheSeaOrALake(ulong seed, int x0, int y0)
    {
        const int Size = 384;
        const int MaxFlood = 400_000;
        const int LakeWidth = 30;
        ProceduralWorldGenerator generator = new(seed);
        ElevationStage elevation = new(seed);
        HashSet<long> done = new();
        int inlandSystems = 0;

        for (int y = y0; y < y0 + Size; y++)
        {
            for (int x = x0; x < x0 + Size; x++)
            {
                if (done.Contains(Key(x, y)) || !IsWater(generator, x, y))
                {
                    continue;
                }

                List<(int X, int Y)> water = new();
                HashSet<long> body = new() { Key(x, y) };
                Queue<(int X, int Y)> open = new();
                open.Enqueue((x, y));
                bool reachesSea = false;
                while (open.Count > 0 && !reachesSea)
                {
                    (int cx, int cy) = open.Dequeue();
                    water.Add((cx, cy));
                    reachesSea = elevation.SampleRoutingElevation(cx, cy) < WorldConstants.SeaLevel;
                    Visit(cx + 1, cy);
                    Visit(cx - 1, cy);
                    Visit(cx, cy + 1);
                    Visit(cx, cy - 1);
                    Assert.True(body.Count < MaxFlood, $"The water body around {x},{y} is too big to follow.");
                }

                done.UnionWith(body);
                if (reachesSea)
                {
                    continue;
                }

                inlandSystems++;
                (int lowX, int lowY) = water[0];
                float lowest = float.MaxValue;
                List<int> widths = new();
                for (int i = 0; i < water.Count; i++)
                {
                    float e = elevation.SampleRoutingElevation(water[i].X, water[i].Y);
                    if (e < lowest)
                    {
                        lowest = e;
                        (lowX, lowY) = water[i];
                    }

                    if ((i % 7) == 0)
                    {
                        widths.Add(OpenWidth(generator, water[i].X, water[i].Y));
                    }
                }

                widths.Sort();
                int channel = widths[widths.Count / 2];
                int pool = 0;
                for (int dy = -8; dy <= 8; dy++)
                {
                    for (int dx = -8; dx <= 8; dx++)
                    {
                        if (IsWater(generator, lowX + dx, lowY + dy))
                        {
                            pool = Math.Max(pool, OpenWidth(generator, lowX + dx, lowY + dy));
                        }
                    }
                }

                Assert.True(
                    pool * 4 >= channel * 5 || channel >= LakeWidth,
                    $"The {water.Count}-tile river system around {x},{y} reaches no sea and ends at {lowX},{lowY} in water {pool} tiles across, no wider than its {channel}-tile channel: it ends on dry land, not in a lake.");

                void Visit(int nx, int ny)
                {
                    if (body.Add(Key(nx, ny)) && IsWater(generator, nx, ny))
                    {
                        open.Enqueue((nx, ny));
                    }
                }
            }
        }

        Assert.True(inlandSystems >= 1, "The window holds no river system that ends inland.");

        static long Key(int x, int y) => ((long)x << 32) | (uint)y;
    }

    private static bool IsWater(ProceduralWorldGenerator generator, int x, int y) =>
        WaterWading.IsWater(generator.SampleTile(x, y).Biome);

    // The narrowest of the four runs of water (across, down and both diagonals) through a tile, in tiles: the width
    // of a channel there, or the size of a pool.
    private static int OpenWidth(ProceduralWorldGenerator generator, int x, int y)
    {
        int best = int.MaxValue;
        foreach ((int dx, int dy, float scale) in new[] { (1, 0, 1f), (0, 1, 1f), (1, 1, 1.4142135f), (1, -1, 1.4142135f) })
        {
            int run = 1;
            for (int sign = -1; sign <= 1; sign += 2)
            {
                for (int k = 1; k < 64 && IsWater(generator, x + (sign * k * dx), y + (sign * k * dy)); k++)
                {
                    run++;
                }
            }

            best = Math.Min(best, (int)(run * scale));
        }

        return best;
    }

    // A sample shore transect (seed 42): from the beach at 954,-6000 due east over open sea to the abyss. Under the
    // old terrain-noise sea floor, depth along such a line rose and fell with every ridge and trough.
    [Fact]
    public void Generate_ShoreTransectIntoOpenSea_GetsDeeperAllTheWayToTheAbyss()
    {
        const int CoastX = 954;
        const int Y = -6000;
        const int Transect = 200;
        ProceduralWorldGenerator generator = new(42UL);
        Assert.False(WaterWading.IsWater(generator.SampleTile(CoastX, Y).Biome), "The transect must start on the beach.");

        float previous = 0f;
        for (int step = 1; step <= Transect; step++)
        {
            MapTile tile = generator.SampleTile(CoastX + step, Y);
            Assert.True(WaterWading.IsWater(tile.Biome), $"The transect must cross open sea; {CoastX + step},{Y} is {tile.Biome}.");
            float depth = WaterDepth.Of(tile);
            Assert.True(depth >= previous, $"Depth falls from {previous:F4} to {depth:F4} {step} tiles out from the coast.");
            previous = depth;
        }

        Assert.Equal(WaterDepthTier.Shore, WaterDepth.TierOf(generator.SampleTile(CoastX + 1, Y)));
        Assert.Equal(WaterDepthTier.Abyss, WaterDepth.TierOf(previous));
    }

    [Theory]
    [InlineData(0f, WaterDepthTier.Dry)]
    [InlineData(WaterDepth.MinDepth, WaterDepthTier.Shore)]
    [InlineData(WaterDepth.ShallowFrom, WaterDepthTier.Shallow)]
    [InlineData(WaterDepth.MediumFrom, WaterDepthTier.Medium)]
    [InlineData(WaterDepth.DeepFrom, WaterDepthTier.Deep)]
    [InlineData(WaterDepth.AbyssFrom, WaterDepthTier.Abyss)]
    public void TierOf_AtEachThreshold_ReturnsThatTier(float depth, WaterDepthTier expected) =>
        Assert.Equal(expected, WaterDepth.TierOf(depth));

    [Fact]
    public void SeaDepthAtCoastDistance_MovingOffshore_StrictlyIncreasesUntilTheAbyssFloor()
    {
        float previous = -1f;
        for (int tiles = 0; tiles <= 220; tiles++)
        {
            float depth = WaterDepth.SeaDepthAtCoastDistance(tiles);
            Assert.True(depth > previous, $"Sea depth {tiles} tiles out ({depth}) should exceed {previous}.");
            previous = depth;
        }

        Assert.Equal(WaterDepthTier.Abyss, WaterDepth.TierOf(previous));
    }

    private sealed class WaterWindow
    {
        private readonly MapTile[] _tiles;
        private readonly bool[] _sea;
        private readonly int _size;

        private WaterWindow(MapTile[] tiles, bool[] sea, int size)
        {
            _tiles = tiles;
            _sea = sea;
            _size = size;
        }

        public static WaterWindow Sample(ulong seed, int x0, int y0, int size)
        {
            ProceduralWorldGenerator generator = new(seed);
            ElevationStage elevation = new(seed);
            MapTile[] tiles = new MapTile[size * size];
            bool[] sea = new bool[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    tiles[(y * size) + x] = generator.SampleTile(x0 + x, y0 + y);
                    sea[(y * size) + x] = elevation.SampleRoutingElevation(x0 + x, y0 + y) < WorldConstants.SeaLevel;
                }
            }

            return new WaterWindow(tiles, sea, size);
        }

        public MapTile Tile(int x, int y) => _tiles[(y * _size) + x];

        public bool IsWater(int x, int y) => WaterWading.IsWater(this.Tile(x, y).Biome);

        // River, lake or oasis water: below the terrain the sea starts at.
        public bool IsInlandWater(int x, int y) => this.IsWater(x, y) && !_sea[(y * _size) + x];

        // The narrower of the horizontal and vertical runs of water through a tile: never less than the true
        // width of the channel there. False when a run reaches the window edge (its length is unknown).
        public bool TryMeasureWidth(int x, int y, out int width)
        {
            width = 0;
            if (!this.TryRun(x, y, 1, 0, out int horizontal) || !this.TryRun(x, y, 0, 1, out int vertical))
            {
                return false;
            }

            width = Math.Min(horizontal, vertical);
            return true;
        }

        private bool TryRun(int x, int y, int dx, int dy, out int length)
        {
            length = 1;
            for (int sign = -1; sign <= 1; sign += 2)
            {
                int nx = x + (sign * dx);
                int ny = y + (sign * dy);
                while (true)
                {
                    if (nx < 0 || ny < 0 || nx >= _size || ny >= _size)
                    {
                        return false;
                    }

                    if (!this.IsWater(nx, ny))
                    {
                        break;
                    }

                    length++;
                    nx += sign * dx;
                    ny += sign * dy;
                }
            }

            return true;
        }
    }
}
