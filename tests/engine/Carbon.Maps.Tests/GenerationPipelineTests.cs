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
    // Pins tiles, props and landmarks over 81 scattered chunks plus 90k pure samples (generator version 20: villages off
    // the road network, wider and drier (ADR-027 revision 2); on top of 19's water depth tiers (ADR-032), 17's starter
    // forage and 18's street villages). Every generated field is hashed by value, never a record's GetHashCode, so a field
    // the generator never sets (such as MapProp.GrowthStage, ADR-040) does not move it. Update it only together with
    // GeneratorVersion.
    private const ulong GoldenWorldFingerprint = 14213290101313462171UL;

    [Fact]
    public void Generate_WorldFingerprint_IsStable() =>
        Assert.Equal(GoldenWorldFingerprint, WorldFingerprint(new ProceduralWorldGenerator(42UL)));

    /// <summary>FNV-1a over every generated tile, prop and landmark field of a fixed sample of the world.</summary>
    internal static ulong WorldFingerprint(ProceduralWorldGenerator generator)
    {
        ulong h = 1469598103934665603UL;
        void Mix(ulong v) => h = (h ^ v) * 1099511628211UL;
        void MixFloat(float v) => Mix(BitConverter.SingleToUInt32Bits(v));
        void MixTile(in MapTile t)
        {
            Mix((ulong)t.Biome);
            MixFloat(t.Elevation);
            MixFloat(t.Temperature);
            MixFloat(t.Moisture);
            Mix(t.IsSolid ? 1UL : 0UL);
            Mix(t.Variant);
            Mix(t.HeightLevel);
        }

        for (int cy = -4; cy <= 4; cy++)
        {
            for (int cx = -4; cx <= 4; cx++)
            {
                Chunk2D chunk = generator.GenerateChunk(cx * 7, cy * 5);
                for (int y = 0; y < Chunk2D.Size; y++)
                {
                    for (int x = 0; x < Chunk2D.Size; x++)
                    {
                        MixTile(chunk.GetTile(x, y));
                    }
                }

                for (int i = 0; i < chunk.Props.Count; i++)
                {
                    MapProp p = chunk.Props[i];
                    Mix((ulong)p.Type);
                    Mix((ulong)p.Category);
                    Mix(p.Destructibility.IsDestructible ? 1UL : 0UL);
                    MixFloat(p.Destructibility.MaxDurability);
                    MixFloat(p.Destructibility.CurrentDurability);
                    Mix(p.Destructibility.HarvestTier);
                    Mix(p.Destructibility.IsDestroyed ? 1UL : 0UL);
                    Mix((uint)p.LocalTileX);
                    Mix((uint)p.LocalTileY);
                    MixFloat(p.WorldPositionX);
                    MixFloat(p.WorldPositionY);
                    MixFloat(p.Radius);
                    MixFloat(p.CollisionRadius);
                    MixFloat(p.CanopyRadius);
                    MixFloat(p.CanopyOffsetY);
                    Mix(p.IsSolid ? 1UL : 0UL);
                    Mix(p.ColorPrimary);
                    Mix(p.ColorSecondary);
                    MixFloat(p.Scale);
                }

                foreach (PointOfInterest poi in chunk.PointsOfInterest)
                {
                    Mix((ulong)poi.Type);
                    Mix((uint)poi.TileX);
                    Mix((uint)poi.TileY);
                    Mix((uint)poi.Radius);
                    MixFloat(poi.FacingX);
                    MixFloat(poi.FacingY);
                }
            }
        }

        for (int y = 0; y < 300; y++)
        {
            for (int x = 0; x < 300; x++)
            {
                MixTile(generator.SampleTile((x * 13) - 2000, (y * 11) + 900));
            }
        }

        return h;
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
