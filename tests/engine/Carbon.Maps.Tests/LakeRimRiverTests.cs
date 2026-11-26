// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Maps.Biomes;
using Carbon.Maps.Generators;
using Xunit;

namespace Carbon.Maps.Tests;

public sealed class LakeRimRiverTests
{
    // A river crossing a lake basin's shallow rim used to follow the priority-flood epsilon gradient in a rigid
    // straight line beside the lake: from above, long vertical stripes of water / land / water (seed 1337, the
    // TropicalJungle framed by the screenshot tool at tile 1120, 632). The rim now joins the lake.
    // Generator v12 measured 29 rows here; v13 measures 2.
    [Fact]
    public void Generate_RiverAcrossLakeRim_DoesNotRunAsStraightStripeBesideLake()
    {
        ProceduralWorldGenerator generator = new(1337UL);

        int longest = LongestParallelLandStrip(generator, 1040, 560, 160, 160);

        Assert.True(longest < 12, $"A thin land strip runs between two waters for {longest} consecutive rows.");
    }

    // Lake shores come from a 16-tile cell grid; without the shore warp they ran down one tile column for
    // 28 rows at the seed 1337 site (v12).
    [Theory]
    [InlineData(42UL, 0, 0)]
    [InlineData(1337UL, 1120, 632)]
    public void Generate_LakeShores_AreNotRulerStraight(ulong seed, int centreX, int centreY)
    {
        ProceduralWorldGenerator generator = new(seed);

        int longest = LongestStraightVerticalShore(generator, centreX - 128, centreY - 128, 256, 256);

        Assert.True(longest < 22, $"A lake or river shore runs straight down one tile column for {longest} rows.");
    }

    // Longest run of consecutive rows in which a land strip 1..12 tiles wide, water on both sides, stays within
    // two tiles of the same column.
    private static int LongestParallelLandStrip(ProceduralWorldGenerator generator, int x0, int y0, int width, int height)
    {
        int[] previousRun = new int[width];
        int[] currentRun = new int[width];
        int longest = 0;
        for (int y = y0; y < y0 + height; y++)
        {
            Array.Clear(currentRun);
            int x = x0;
            while (x < x0 + width)
            {
                if (!IsWater(generator, x, y) || !IsWater(generator, x - 1, y))
                {
                    x++;
                    continue;
                }

                // x - 1 is water, x is water: find the next land run.
                int start = x;
                while (start < x0 + width && IsWater(generator, start, y))
                {
                    start++;
                }

                int end = start;
                while (end < x0 + width && !IsWater(generator, end, y))
                {
                    end++;
                }

                int stripWidth = end - start;
                if (end < x0 + width && stripWidth is >= 1 and <= 12)
                {
                    int centre = ((start + end) / 2) - x0;
                    int run = 1;
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int c = centre + dx;
                        if ((uint)c < (uint)width)
                        {
                            run = Math.Max(run, previousRun[c] + 1);
                        }
                    }

                    currentRun[centre] = run;
                    longest = Math.Max(longest, run);
                }

                x = Math.Max(end, x + 1);
            }

            (previousRun, currentRun) = (currentRun, previousRun);
        }

        return longest;
    }

    // Longest run of consecutive rows whose land/water boundary sits on exactly the same tile column.
    private static int LongestStraightVerticalShore(ProceduralWorldGenerator generator, int x0, int y0, int width, int height)
    {
        int[] run = new int[width];
        int longest = 0;
        for (int y = y0; y < y0 + height; y++)
        {
            bool left = IsWater(generator, x0, y);
            for (int i = 1; i < width; i++)
            {
                bool right = IsWater(generator, x0 + i, y);
                run[i] = left != right ? run[i] + 1 : 0;
                longest = Math.Max(longest, run[i]);
                left = right;
            }
        }

        return longest;
    }

    private static bool IsWater(ProceduralWorldGenerator generator, int x, int y) =>
        generator.SampleTile(x, y).Biome is BiomeType.ShallowWater or BiomeType.DeepOcean;
}
