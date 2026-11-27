// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Maps.Generators.Stages;
using Carbon.Maps.Hydrology;
using FluentAssertions;
using Xunit;

namespace Carbon.Maps.Tests;

/// <summary>
/// ADR-045 D7: how the river network caches its regions. Runs in the generation group, since the cache is shared by
/// every network in the process today.
/// </summary>
[Collection(GenerationAllocationGroup.Name)]
public sealed class HydrologyCacheTests
{
    private const ulong Seed = 4242UL;
    private const float Frequency = 0.0008f;

    [Fact]
    public void GetDistance_TwoNetworksWithTheSameSeed_ShareTheirRegions()
    {
        // Characterisation: the cache is process-wide, keyed by seed, so a second world finds the first's regions.
        RiverNetwork first = Create(maxCachedRegions: 128);
        RiverNetwork second = Create(maxCachedRegions: 128);

        _ = first.GetDistance(RegionCentre(40), RegionCentre(0));
        _ = second.GetDistance(RegionCentre(40), RegionCentre(0));

        first.RegionBuilds.Should().Be(1);
        second.RegionBuilds.Should().Be(0, "the second network reads the region the first built");
    }

    [Fact]
    public void GetDistance_MoreRegionsThanTheBound_RebuildsTheFirstRegionStillInUse()
    {
        // Characterisation: trimming is first in, first out, whatever is still in use. The queue is process-wide, so
        // other tests' regions may be ahead of this one; each insert trims up to 8, and 40 inserts reach it.
        RiverNetwork network = Create(maxCachedRegions: 3);
        _ = network.GetDistance(RegionCentre(60), RegionCentre(0));
        for (int rx = 61; rx <= 100; rx++)
        {
            _ = network.GetDistance(RegionCentre(rx), RegionCentre(0));
        }

        int builds = network.RegionBuilds;
        _ = network.GetDistance(RegionCentre(60), RegionCentre(0));

        network.RegionBuilds.Should().Be(builds + 1, "the first region was trimmed and is built again");
    }

    private static RiverNetwork Create(int maxCachedRegions) =>
        new(Seed, Frequency, new ElevationStage(Seed, Frequency), maxCachedRegions);

    private static int RegionCentre(int region) => (region * RiverNetwork.RegionSize) + (RiverNetwork.RegionSize / 2);
}
