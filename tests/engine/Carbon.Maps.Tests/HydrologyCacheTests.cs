// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Maps.Generators;
using Carbon.Maps.Generators.Stages;
using Carbon.Maps.Hydrology;
using Carbon.Maps.Streaming;
using FluentAssertions;
using Xunit;

namespace Carbon.Maps.Tests;

/// <summary>
/// ADR-045 D7, D9 and D10: each world's river network caches its own regions, keeps the regions its players are near,
/// builds them without the world's locks, and stops its background builds cleanly. Runs in the generation group, so
/// the thread-pool timing of other tests cannot stretch the background builds past the waits below.
/// </summary>
[Collection(GenerationAllocationGroup.Name)]
public sealed class HydrologyCacheTests
{
    private const ulong Seed = 4242UL;
    private const float Frequency = 0.0008f;
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public void GetDistance_TwoWorldsWithTheSameSeed_KeepTheirOwnRegions()
    {
        RiverNetwork first = Create(maxCachedRegions: 128);
        RiverNetwork second = Create(maxCachedRegions: 3);

        _ = first.GetDistance(RegionCentre(40), RegionCentre(0));
        _ = second.GetDistance(RegionCentre(40), RegionCentre(0));
        for (int rx = 41; rx <= 60; rx++)
        {
            _ = second.GetDistance(RegionCentre(rx), RegionCentre(0));
        }

        _ = first.GetDistance(RegionCentre(40), RegionCentre(0));

        first.RegionBuilds.Should().Be(1, "the first world's region stays cached, however the second world trims its own");
        second.RegionBuilds.Should().Be(21, "the second world builds its own regions instead of reading the first's");
    }

    [Fact]
    public void GetDistance_MoreRegionsThanTheBound_KeepsTheRegionAPlayerIsNear()
    {
        RiverNetwork network = Create(maxCachedRegions: 3);
        Prewarm(network, RegionCentre(60), stream: 7UL);
        int builds = network.RegionBuilds;

        for (int rx = 61; rx <= 100; rx++)
        {
            _ = network.GetDistance(RegionCentre(rx), RegionCentre(0));
        }

        _ = network.GetDistance(RegionCentre(60), RegionCentre(0));
        network.RegionBuilds.Should().Be(builds + 40, "the pinned region is read from the cache, not built again");
    }

    [Fact]
    public void ReleaseStream_ThePlayerLeft_LetsItsRegionBeTrimmed()
    {
        RiverNetwork network = Create(maxCachedRegions: 3);
        Prewarm(network, RegionCentre(60), stream: 7UL);
        network.ReleaseStream(7UL);

        for (int rx = 61; rx <= 100; rx++)
        {
            _ = network.GetDistance(RegionCentre(rx), RegionCentre(0));
        }

        int builds = network.RegionBuilds;
        _ = network.GetDistance(RegionCentre(60), RegionCentre(0));
        network.RegionBuilds.Should().Be(builds + 1, "once released, the region is trimmed like any other");
    }

    [Fact]
    public void Prewarm_PlayersSpreadPastTheBound_GrowsTheBoundToHoldEveryPinnedRegion()
    {
        // Two players ten regions apart pin 3 x 3 regions each, 18 in all, against a bound of 3.
        RiverNetwork network = Create(maxCachedRegions: 3);
        int radius = RiverNetwork.RegionSize;
        Prewarm(network, RegionCentre(70), stream: 1UL, radius);
        Prewarm(network, RegionCentre(80), stream: 2UL, radius);
        int builds = network.RegionBuilds;

        for (int rx = 100; rx <= 130; rx++)
        {
            _ = network.GetDistance(RegionCentre(rx), RegionCentre(0));
        }

        int afterOthers = network.RegionBuilds;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                _ = network.GetDistance(RegionCentre(70 + dx), RegionCentre(dy));
                _ = network.GetDistance(RegionCentre(80 + dx), RegionCentre(dy));
            }
        }

        builds.Should().Be(18);
        network.RegionBuilds.Should().Be(afterOthers, "none of the 18 pinned regions was trimmed");
        network.CachedRegions.Should().BeGreaterThanOrEqualTo(18);
    }

    [Fact]
    public void GetDistance_PinnedRegionsFillTheBound_StillKeepsARegionJustStreamedIn()
    {
        // The bound grows past the pinned regions by a margin, so the region a player is about to walk into is not
        // trimmed by the next insert just because the pins fill the plain bound.
        RiverNetwork network = Create(maxCachedRegions: 3);
        int radius = RiverNetwork.RegionSize;
        Prewarm(network, RegionCentre(70), stream: 1UL, radius);
        Prewarm(network, RegionCentre(80), stream: 2UL, radius);

        for (int rx = 90; rx < 100; rx++)
        {
            _ = network.GetDistance(RegionCentre(rx), RegionCentre(0));
        }

        int builds = network.RegionBuilds;
        for (int rx = 90; rx < 100; rx++)
        {
            _ = network.GetDistance(RegionCentre(rx), RegionCentre(0));
        }

        network.RegionBuilds.Should().Be(builds, "ten unpinned regions fit in the margin above the 18 pinned ones, so none was trimmed");
    }

    [Fact]
    public void PrewarmHydrology_WhileTheWorldGateIsHeld_StillCompletesItsBuilds()
    {
        // ADR-045 D9: a background build never takes the world's locks, so it finishes while the tick holds them.
        ChunkWorld2D world = new(new ProceduralWorldGenerator(Seed), tileSize: 32f);
        RiverNetwork rivers = world.Generator.Rivers;
        bool started;
        bool finished;
        lock (world.Gate)
        {
            world.Generator.PrewarmHydrology(RegionCentre(90), RegionCentre(90), RiverNetwork.RegionSize, stream: 3UL);
            started = rivers.PendingBuilds > 0;
            finished = SpinWait.SpinUntil(() => rivers.PendingBuilds == 0, s_timeout);
        }

        started.Should().BeTrue("the prewarm started background builds");
        finished.Should().BeTrue("the builds completed while this thread held the world gate");
        rivers.RegionBuilds.Should().Be(9);
    }

    [Fact]
    public void StopBackgroundBuilds_WithBuildsQueuedAndRunning_StopsThemAllAndLeavesNoUnobservedException()
    {
        // ADR-045 D10.
        List<Exception> unobserved = [];
        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            lock (unobserved)
            {
                unobserved.Add(e.Exception);
            }
        }

        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            RiverNetwork network = Create(maxCachedRegions: 128);
            network.Prewarm(RegionCentre(200), RegionCentre(200), RiverNetwork.RegionSize * 3, stream: 1UL);
            network.PendingBuilds.Should().BeGreaterThan(0, "the prewarm queued background builds");

            network.StopBackgroundBuilds(s_timeout).Should().BeTrue("every build finished or was skipped");
            network.PendingBuilds.Should().Be(0);
            network.RegionBuilds.Should().BeLessThan(49, "builds not started before the stop were skipped");

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            lock (unobserved)
            {
                unobserved.Should().BeEmpty("every background build was observed");
            }
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }
    }

    private static RiverNetwork Create(int maxCachedRegions) =>
        new(Seed, new ElevationStage(Seed, Frequency), maxCachedRegions);

    // Prewarms around a region centre and waits for its background builds.
    private static void Prewarm(RiverNetwork network, int centreX, ulong stream, int radius = 0)
    {
        network.Prewarm(centreX, RegionCentre(0), radius, stream);
        SpinWait.SpinUntil(() => network.PendingBuilds == 0, s_timeout).Should().BeTrue("the prewarm builds finish");
    }

    private static int RegionCentre(int region) => (region * RiverNetwork.RegionSize) + (RiverNetwork.RegionSize / 2);
}
