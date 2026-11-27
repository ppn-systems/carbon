// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
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

namespace Carbon.Desktop.Tests;

public sealed class TerrainBakeTests
{
    private const ulong Seed = 1337UL;

    [Fact]
    public void BakeChunkGeometry_SameChunkTwice_ProducesIdenticalGeometry()
    {
        CarbonVectorArtLibrary art = LoadArt();
        ChunkWorld2D world = new(new ProceduralWorldGenerator(Seed));
        Chunk2D chunk = StreamAround(world, world.SpawnTile);
        ChunkMeshBuilder builder = new();
        ChunkGeometryBaker baker = new();

        baker.Bake(builder, chunk, world, art);
        int first = builder.VertexCount;
        builder.Clear();
        baker.Bake(builder, chunk, world, art);

        first.Should().BeGreaterThan(Chunk2D.Size * Chunk2D.Size * 5, "every tile bakes at least its 5-vertex gradient quad");
        builder.VertexCount.Should().Be(first);
    }

    [Fact]
    public void Bake_TwoBakersInterleaved_KeepTheirOwnWaterFeatures()
    {
        // Audit TS-11: the scratch used to be static, so a second renderer's bake overwrote the first one's results.
        CarbonVectorArtLibrary art = LoadArt();
        ProceduralWorldGenerator generator = new(Seed);
        ChunkWorld2D world = new(generator);
        (int sx, int sy) = generator.SpawnTile;
        BiomeLocator.TryFind(generator, BiomeType.ShallowWater, sx, sy, 2400, out int wx, out int wy).Should().BeTrue();
        Chunk2D water = StreamAround(world, (wx, wy));
        Chunk2D land = StreamAround(world, world.SpawnTile);
        ChunkGeometryBaker waterBaker = new();
        ChunkGeometryBaker landBaker = new();

        waterBaker.Bake(new ChunkMeshBuilder(), water, world, art);
        RippleSpotList before = new(waterBaker.Ripples);
        landBaker.Bake(new ChunkMeshBuilder(), land, world, art);

        before.Items.Should().NotBeEmpty("a shallow-water chunk has animated ripples");
        waterBaker.Ripples.Should().Equal(before.Items, "another baker's bake must not touch this baker's results");
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

    private sealed record RippleSpotList(IReadOnlyList<ChunkTerrainRenderer.RippleSpot> Source)
    {
        public List<ChunkTerrainRenderer.RippleSpot> Items { get; } = [.. Source];
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
