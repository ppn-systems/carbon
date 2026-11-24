// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Maps.Biomes;
using Carbon.Maps.Topography;
using Xunit;

namespace Carbon.Maps.Tests;

public sealed class TopographyAndCliffAutotilerTests
{
    [Fact]
    public void TopographyHelper_ResolvesHeightLevels()
    {
        // Water is always HeightLevel 0
        Assert.Equal(0, TopographyHelper.ResolveHeightLevel(0.1f, BiomeType.DeepOcean));
        Assert.Equal(0, TopographyHelper.ResolveHeightLevel(0.3f, BiomeType.ShallowWater));

        // Lowland plains (elevation < 0.68f)
        Assert.Equal(0, TopographyHelper.ResolveHeightLevel(0.45f, BiomeType.LushPlains));
        Assert.Equal(0, TopographyHelper.ResolveHeightLevel(0.60f, BiomeType.AncientForest));

        // Highland plateaus (0.68f <= elevation < 0.82f)
        Assert.Equal(1, TopographyHelper.ResolveHeightLevel(0.70f, BiomeType.LushPlains));
        Assert.Equal(1, TopographyHelper.ResolveHeightLevel(0.80f, BiomeType.RedMesa));

        // Mountain peaks (elevation >= 0.82f)
        Assert.Equal(2, TopographyHelper.ResolveHeightLevel(0.85f, BiomeType.VolcanicAshland));
        Assert.Equal(2, TopographyHelper.ResolveHeightLevel(0.95f, BiomeType.FrozenTundra));
    }

    [Fact]
    public void CliffAutotiler_ComputesCardinalDropMasks()
    {
        // Grid setup: Center at (5, 5) is HeightLevel 1 (Plateau)
        // North (5, 4) is HeightLevel 0 (Lowland) -> Drop North
        // South (5, 6) is HeightLevel 0 (Lowland) -> Drop South
        // East (6, 5) is HeightLevel 1 (Same Plateau) -> No drop
        // West (4, 5) is HeightLevel 1 (Same Plateau) -> No drop
        static byte GetHeight(int x, int y)
        {
            if (x == 5 && y == 5)
            {
                return 1;
            }

            if (x == 6 && y == 5)
            {
                return 1;
            }

            if (x == 4 && y == 5)
            {
                return 1;
            }

            return 0;
        }

        CliffFaceMask mask = CliffAutotiler.ComputeCliffMask(5, 5, GetHeight);

        Assert.True(mask.HasFlag(CliffFaceMask.North));
        Assert.True(mask.HasFlag(CliffFaceMask.South));
        Assert.False(mask.HasFlag(CliffFaceMask.East));
        Assert.False(mask.HasFlag(CliffFaceMask.West));
        Assert.Equal(CliffFaceMask.North | CliffFaceMask.South, mask);
    }

    [Fact]
    public void CliffAutotiler_SteppingAcrossLevelsRequiresAlignedRamp()
    {
        // Center is (10, 10) at HeightLevel 0 (Lowland)
        // North is (10, 9) at HeightLevel 1 (Plateau)
        static byte GetHeight(int x, int y) => (byte)(y <= 9 ? 1 : 0);

        // Case 1: No ramp -> Stepping from (10, 10) to (10, 9) is IMPASSABLE
        bool canStepNoRamp = CliffAutotiler.CanStepAcross(10, 10, 10, 9, GetHeight, (_, _) => RampDirection.None);
        Assert.False(canStepNoRamp);

        // Case 2: Ramp aligned North-South at (10, 10) -> PASSABLE
        static RampDirection GetRampNorth(int x, int y) => x == 10 && y == 10 ? RampDirection.North : RampDirection.None;
        bool canStepNorthRamp = CliffAutotiler.CanStepAcross(10, 10, 10, 9, GetHeight, GetRampNorth);
        Assert.True(canStepNorthRamp);

        // Case 3: Walking back down South from (10, 9) to (10, 10) -> PASSABLE
        bool canStepSouthDown = CliffAutotiler.CanStepAcross(10, 9, 10, 10, GetHeight, GetRampNorth);
        Assert.True(canStepSouthDown);

        // Case 4: Misaligned ramp (East-West ramp) -> IMPASSABLE along North-South axis
        static RampDirection GetRampEast(int x, int y) => x == 10 && y == 10 ? RampDirection.East : RampDirection.None;
        bool canStepMisaligned = CliffAutotiler.CanStepAcross(10, 10, 10, 9, GetHeight, GetRampEast);
        Assert.False(canStepMisaligned);

        // Case 5: Same height level -> ALWAYS PASSABLE
        bool canStepSameLevel = CliffAutotiler.CanStepAcross(10, 10, 11, 10, GetHeight, (_, _) => RampDirection.None);
        Assert.True(canStepSameLevel);

        // Case 6: Cliff drop > 1 level (Level 0 to Level 2) -> ALWAYS IMPASSABLE even with ramp
        static byte GetExtremeHeight(int x, int y) => (byte)(y <= 9 ? 2 : 0);
        bool canStepExtreme = CliffAutotiler.CanStepAcross(10, 10, 10, 9, GetExtremeHeight, GetRampNorth);
        Assert.False(canStepExtreme);
    }
}
