// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Maps.Biomes;
using Carbon.Maps.Generators;
using Xunit;

namespace Carbon.Maps.Tests;

public sealed class BiomeLocatorTests
{
    private const ulong Seed = 1337UL;

    [Theory]
    [InlineData(BiomeType.LushPlains)]
    [InlineData(BiomeType.ShallowWater)]
    [InlineData(BiomeType.AncientForest)]
    public void TryFind_NearSpawn_ReturnsRepresentativeTileOfBiome(BiomeType biome)
    {
        ProceduralWorldGenerator generator = new(Seed);
        (int sx, int sy) = generator.SpawnTile;

        bool found = BiomeLocator.TryFind(generator, biome, sx, sy, 2400, out int x, out int y);

        Assert.True(found, $"{biome} should exist within 2400 tiles of spawn");
        Assert.Equal(biome, generator.SampleTile(x, y).Biome);
        Assert.True(BiomeLocator.IsRepresentative(generator, biome, x, y));
    }

    [Fact]
    public void TryFind_SameSeed_IsDeterministic()
    {
        ProceduralWorldGenerator a = new(Seed);
        ProceduralWorldGenerator b = new(Seed);

        Assert.True(BiomeLocator.TryFind(a, BiomeType.AncientForest, 0, 0, 2400, out int ax, out int ay));
        Assert.True(BiomeLocator.TryFind(b, BiomeType.AncientForest, 0, 0, 2400, out int bx, out int by));

        Assert.Equal((ax, ay), (bx, by));
    }

    [Fact]
    public void IsRepresentative_ShallowWater_RejectsTilesTouchingLand()
    {
        ProceduralWorldGenerator generator = new(Seed);
        (int sx, int sy) = generator.SpawnTile;
        Assert.True(BiomeLocator.TryFind(generator, BiomeType.ShallowWater, sx, sy, 2400, out int x, out int y));

        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                BiomeType b = generator.SampleTile(x + dx, y + dy).Biome;
                Assert.True(b is BiomeType.ShallowWater or BiomeType.DeepOcean);
            }
        }
    }
}
