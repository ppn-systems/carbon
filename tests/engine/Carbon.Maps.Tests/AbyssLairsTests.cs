// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Maps.Generators;
using Carbon.Maps.Hydrology;
using Xunit;

namespace Carbon.Maps.Tests;

// ADR-032 D15: abyss lairs are fixed by world data, rare, and always sit in open abyss.
public sealed class AbyssLairsTests
{
    private const ulong Seed = 42UL;

    // Seed 42's sea north of the coast at tile (950, -6000).
    private static readonly (int X, int Y)[] s_seaCells = [(0, -8), (1, -6), (2, -6), (3, -6), (0, -5), (1, -5), (0, -4), (3, -8)];

    [Fact]
    public void TryGetLair_SameSeed_GivesTheSameAnchors()
    {
        ProceduralWorldGenerator generator = new(Seed);
        AbyssLairs a = new(generator, Seed, 1f);
        AbyssLairs b = new(new ProceduralWorldGenerator(Seed), Seed, 1f);

        foreach ((int x, int y) in s_seaCells)
        {
            Assert.Equal(a.TryGetLair(x, y, out (int X, int Y) first), b.TryGetLair(x, y, out (int X, int Y) second));
            Assert.Equal(first, second);
        }
    }

    [Fact]
    public void TryGetLair_Anchor_SitsInOpenAbyssWithAbyssAllAround()
    {
        ProceduralWorldGenerator generator = new(Seed);
        AbyssLairs lairs = new(generator, Seed, 1f);
        int found = 0;

        foreach ((int x, int y) in s_seaCells)
        {
            if (!lairs.TryGetLair(x, y, out (int X, int Y) anchor))
            {
                continue;
            }

            found++;
            Assert.Equal((x, y), AbyssLairs.CellOf(anchor.X, anchor.Y));
            for (int dy = -AbyssLairs.OpenRadius; dy <= AbyssLairs.OpenRadius; dy += AbyssLairs.OpenRadius)
            {
                for (int dx = -AbyssLairs.OpenRadius; dx <= AbyssLairs.OpenRadius; dx += AbyssLairs.OpenRadius)
                {
                    Assert.Equal(WaterDepthTier.Abyss, WaterDepth.TierOf(generator.SampleTile(anchor.X + dx, anchor.Y + dy)));
                }
            }
        }

        Assert.True(found >= 3, $"Seed 42's open sea should hold lair sites, found {found}.");
    }

    [Fact]
    public void TryGetLair_WorldSpawnOnLand_HasNoLairEvenAtFullChance()
    {
        ProceduralWorldGenerator generator = new(Seed);
        (int sx, int sy) = generator.SpawnTile;
        (int cx, int cy) = AbyssLairs.CellOf(sx, sy);

        Assert.False(new AbyssLairs(generator, Seed, 1f).TryGetLair(cx, cy, out _), "the start area has no open abyss");
    }

    [Fact]
    public void TryGetLair_LairChance_MakesLairsRareAndOnlyWhereTheSeaAllowsThem()
    {
        ProceduralWorldGenerator generator = new(Seed);
        AbyssLairs everywhere = new(generator, Seed, 1f);
        AbyssLairs rare = new(generator, Seed, 0.35f);
        AbyssLairs none = new(generator, Seed, 0f);
        int open = 0;
        int lairs = 0;

        foreach ((int x, int y) in s_seaCells)
        {
            bool possible = everywhere.TryGetLair(x, y, out (int X, int Y) site);
            if (rare.TryGetLair(x, y, out (int X, int Y) lair))
            {
                lairs++;
                Assert.True(possible, $"cell {x},{y} got a lair without open abyss");
                Assert.Equal(site, lair);
            }

            open += possible ? 1 : 0;
            Assert.False(none.TryGetLair(x, y, out _));
        }

        Assert.True(lairs > 0 && lairs < open, $"{lairs} of {open} open-abyss cells hold a lair at a 35 % chance.");
    }

    [Fact]
    public void TryGetLair_AnotherSeed_PutsTheLairsElsewhere()
    {
        ProceduralWorldGenerator generator = new(Seed);
        AbyssLairs seed42 = new(generator, Seed, 1f);
        AbyssLairs other = new(generator, Seed + 1UL, 1f);
        int differ = 0;

        foreach ((int x, int y) in s_seaCells)
        {
            bool a = seed42.TryGetLair(x, y, out (int X, int Y) first);
            bool b = other.TryGetLair(x, y, out (int X, int Y) second);
            differ += a != b || first != second ? 1 : 0;
        }

        Assert.True(differ > 0, "the lair hash must depend on the seed");
    }
}
