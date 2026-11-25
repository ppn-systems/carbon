// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Generators.Stages;
using Carbon.Maps.Landmarks;
using Carbon.Maps.Props;
using Xunit;

namespace Carbon.Maps.Tests;

public sealed class GenerationPipelineTests
{
    // Pins tiles (all fields), props and landmarks over 81 scattered chunks plus 90k pure samples (generator
    // version 12: MMORPG worldgen overhaul). Update it only together with GeneratorVersion.
    private const ulong GoldenWorldFingerprint = 2276200943289429033UL;

    [Fact]
    public void Generate_WorldFingerprint_IsStable()
    {
        ulong h = 1469598103934665603UL;
        void Mix(ulong v) => h = (h ^ v) * 1099511628211UL;

        ProceduralWorldGenerator generator = new(42UL);
        for (int cy = -4; cy <= 4; cy++)
        {
            for (int cx = -4; cx <= 4; cx++)
            {
                Chunk2D chunk = generator.GenerateChunk(cx * 7, cy * 5);
                for (int y = 0; y < Chunk2D.Size; y++)
                {
                    for (int x = 0; x < Chunk2D.Size; x++)
                    {
                        MapTile t = chunk.GetTile(x, y);
                        Mix((ulong)t.GetHashCode());
                        Mix(BitConverter.SingleToUInt32Bits(t.Temperature));
                        Mix(BitConverter.SingleToUInt32Bits(t.Moisture));
                    }
                }

                for (int i = 0; i < chunk.Props.Count; i++)
                {
                    Mix((ulong)chunk.Props[i].GetHashCode());
                }

                foreach (PointOfInterest poi in chunk.PointsOfInterest)
                {
                    Mix((ulong)poi.GetHashCode());
                }
            }
        }

        for (int y = 0; y < 300; y++)
        {
            for (int x = 0; x < 300; x++)
            {
                Mix((ulong)generator.SampleTile((x * 13) - 2000, (y * 11) + 900).GetHashCode());
            }
        }

        Assert.Equal(GoldenWorldFingerprint, h);
    }

    [Fact]
    public void ExtraChunkStage_RunsAfterBuiltIns_WithoutChangingThem()
    {
        CountingStage stage = new();
        Chunk2D plain = new ProceduralWorldGenerator(42UL).GenerateChunk(3, -2);
        Chunk2D extended = new ProceduralWorldGenerator(42UL, extraChunkStages: [stage]).GenerateChunk(3, -2);

        Assert.Equal(1, stage.Calls);
        Assert.Equal(Chunk2D.Size * Chunk2D.Size, stage.TilesSeen);
        Assert.Equal(plain.Props.Count, stage.PropsAlreadyPlaced);
        for (int i = 0; i < plain.Props.Count; i++)
        {
            Assert.Equal(plain.Props[i], extended.Props[i]);
        }
    }

    [Fact]
    public void StageSeed_Derive_IsStableAndDistinctPerStage()
    {
        Assert.Equal(StageSeed.Derive(42UL, "caves"), StageSeed.Derive(42UL, "caves"));
        Assert.NotEqual(StageSeed.Derive(42UL, "caves"), StageSeed.Derive(42UL, "roads"));
        Assert.NotEqual(StageSeed.Derive(42UL, "caves"), StageSeed.Derive(43UL, "caves"));
    }

    [Fact]
    public void BiomePropTables_EveryLandBiome_HasOresAndVegetation()
    {
        foreach (BiomeType biome in Enum.GetValues<BiomeType>())
        {
            BiomePropTable table = BiomePropTables.Get(biome);
            if (biome is BiomeType.DeepOcean or BiomeType.ShallowWater)
            {
                Assert.Same(BiomePropTable.Empty, table);
                continue;
            }

            Assert.NotEmpty(table.Ores);
            Assert.NotEmpty(table.Layers);
        }
    }

    private sealed class CountingStage : IChunkStage
    {
        public int Calls { get; private set; }

        public int TilesSeen { get; private set; }

        public int PropsAlreadyPlaced { get; private set; }

        public void Populate(Chunk2D chunk, ChunkContext context)
        {
            this.Calls++;
            this.TilesSeen = context.Tiles.Length;
            this.PropsAlreadyPlaced = chunk.Props.Count;
        }
    }
}
