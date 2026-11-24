// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Maps.Biomes;
using Xunit;

namespace Carbon.Maps.Tests;

public sealed class WhittakerBiomeResolverTests
{
    [Theory]
    [InlineData(0.10f, 0.5f, 0.5f, BiomeType.DeepOcean)]
    [InlineData(0.28f, 0.5f, 0.5f, BiomeType.ShallowWater)]
    [InlineData(0.35f, 0.5f, 0.5f, BiomeType.BeachSand)]
    [InlineData(0.85f, 0.2f, 0.5f, BiomeType.FrozenTundra)]
    [InlineData(0.85f, 0.85f, 0.5f, BiomeType.VolcanicAshland)]
    [InlineData(0.50f, 0.15f, 0.5f, BiomeType.FrozenTundra)]
    [InlineData(0.50f, 0.80f, 0.15f, BiomeType.DuneDesert)]
    [InlineData(0.50f, 0.75f, 0.85f, BiomeType.TropicalJungle)]
    [InlineData(0.50f, 0.50f, 0.85f, BiomeType.MistySwamp)]
    [InlineData(0.50f, 0.50f, 0.60f, BiomeType.AncientForest)]
    [InlineData(0.50f, 0.50f, 0.40f, BiomeType.LushPlains)]
    [InlineData(0.90f, 0.10f, 0.50f, BiomeType.AlpinePeaks)]
    [InlineData(0.50f, 0.65f, 0.35f, BiomeType.Savanna)]
    [InlineData(0.50f, 0.50f, 0.75f, BiomeType.BlossomWoods)]
    public void Resolve_CorrectlyMapsWhittakerBiomes(
        float elevation,
        float temperature,
        float moisture,
        BiomeType expectedBiome)
    {
        BiomeType actual = WhittakerBiomeResolver.Resolve(elevation, temperature, moisture);
        Assert.Equal(expectedBiome, actual);
    }

    [Fact]
    public void BiomeProperties_AllBiomesHaveDefinedProperties()
    {
        foreach (BiomeType biome in Enum.GetValues<BiomeType>())
        {
            ref readonly BiomeProperties props = ref BiomeProperties.Get(biome);
            Assert.Equal(biome, props.Type);

            if (biome is BiomeType.DeepOcean or BiomeType.VolcanicAshland)
            {
                Assert.True(props.IsSolid);
                Assert.Equal(0f, props.MoveSpeedMultiplier);
            }
            else
            {
                Assert.False(props.IsSolid);
                Assert.True(props.MoveSpeedMultiplier > 0f);
            }
        }
    }
}
