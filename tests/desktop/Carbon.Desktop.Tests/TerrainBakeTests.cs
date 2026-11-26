// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Desktop.Rendering.World;
using Carbon.Graphics.Batching.Procedural;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Streaming;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Carbon.Desktop.Tests;

public sealed class TerrainBakeTests(ITestOutputHelper output)
{
    private const ulong Seed = 1337UL;

    private static readonly BiomeType[] s_benchBiomes =
    [
        BiomeType.LushPlains, BiomeType.AncientForest, BiomeType.TropicalJungle, BiomeType.MistySwamp,
        BiomeType.DuneDesert, BiomeType.RedMesa, BiomeType.BeachSand, BiomeType.FrozenTundra, BiomeType.ShallowWater,
    ];

    [Fact]
    public void BakeChunkGeometry_SameChunkTwice_ProducesIdenticalGeometry()
    {
        CarbonVectorArtLibrary art = LoadArt();
        ChunkWorld2D world = new(new ProceduralWorldGenerator(Seed));
        Chunk2D chunk = StreamAround(world, world.SpawnTile);
        ChunkMeshBuilder builder = new();

        ChunkTerrainRenderer.BakeChunkGeometry(builder, chunk, world, art);
        int first = builder.VertexCount;
        builder.Clear();
        ChunkTerrainRenderer.BakeChunkGeometry(builder, chunk, world, art);

        first.Should().BeGreaterThan(Chunk2D.Size * Chunk2D.Size * 5, "every tile bakes at least its 5-vertex gradient quad");
        builder.VertexCount.Should().Be(first);
    }

    [Fact]
    public void BiomeBlueprints_EveryBiome_DefinesSurfaceAndDecalLayers()
    {
        CarbonVectorArtLibrary art = LoadArt();
        foreach (BiomeType biome in Enum.GetValues<BiomeType>())
        {
            art.TryGetBiome((byte)biome, out CompiledBiomeArt? biomeArt).Should().BeTrue();
            biomeArt!.Surface.Colors.Should().NotBeEmpty();
            biomeArt.DecalLayers.Should().NotBeEmpty($"{biome} should scatter ground decals");
            foreach (BiomeDecalLayer layer in biomeArt.DecalLayers)
            {
                foreach (BiomeDecalVariant variant in layer.Variants)
                {
                    (variant.Max.X - variant.Min.X).Should().BeLessThan(30f, $"{biome}/{layer.Id} decals must fit inside a tile");
                    (variant.Max.Y - variant.Min.Y).Should().BeLessThan(30f, $"{biome}/{layer.Id} decals must fit inside a tile");
                }
            }
        }
    }

    // Reports the CPU cost of a chunk bake per biome (run with --logger "console;verbosity=detailed").
    [Fact]
    public void BakeChunkGeometry_RepresentativeBiomes_ReportsBakeCost()
    {
        CarbonVectorArtLibrary art = LoadArt();
        ProceduralWorldGenerator generator = new(Seed);
        ChunkWorld2D world = new(generator);
        ChunkMeshBuilder builder = new();
        (int sx, int sy) = generator.SpawnTile;

        List<(BiomeType Biome, Chunk2D Chunk)> chunks = [];
        foreach (BiomeType biome in s_benchBiomes)
        {
            if (BiomeLocator.TryFind(generator, biome, sx, sy, 2400, out int bx, out int by))
            {
                chunks.Add((biome, StreamAround(world, (bx, by))));
            }
        }

        // Let tiered JIT promote every bake path to optimized code first, as it is after a minute of play.
        for (int i = 0; i < 40; i++)
        {
            foreach ((BiomeType _, Chunk2D chunk) in chunks)
            {
                builder.Clear();
                ChunkTerrainRenderer.BakeChunkGeometry(builder, chunk, world, art);
            }
        }

        double totalMs = 0d;
        long totalVertices = 0;
        int bakes = 0;
        foreach ((BiomeType biome, Chunk2D chunk) in chunks)
        {
            // The minimum of many runs filters out noise from other processes sharing the machine.
            double ms = double.MaxValue;
            for (int i = 0; i < 25; i++)
            {
                builder.Clear();
                long start = Stopwatch.GetTimestamp();
                ChunkTerrainRenderer.BakeChunkGeometry(builder, chunk, world, art);
                ms = Math.Min(ms, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            }

            output.WriteLine($"{biome,-16} {ms,6:F2} ms  {builder.VertexCount,6} vertices");
            totalMs += ms;
            totalVertices += builder.VertexCount;
            bakes++;
        }

        bakes.Should().BeGreaterThan(0);
        output.WriteLine($"{"average",-16} {totalMs / bakes,6:F2} ms  {totalVertices / bakes,6} vertices");
    }

    private static CarbonVectorArtLibrary LoadArt() =>
        CarbonVectorArtLibrary.LoadOrDefault() ?? throw new InvalidOperationException("resources/art must be available to the tests");

    private static Chunk2D StreamAround(ChunkWorld2D world, (int X, int Y) tile)
    {
        Vector2 pos = new((tile.X + 0.5f) * world.TileSize, (tile.Y + 0.5f) * world.TileSize);
        world.UpdateAroundPosition(pos, chunkRadius: 1, evictionRadius: 400);
        world.TryGetChunk(Chunk2D.ToChunkCoord(tile.X), Chunk2D.ToChunkCoord(tile.Y), out Chunk2D? chunk).Should().BeTrue();
        return chunk!;
    }
}
