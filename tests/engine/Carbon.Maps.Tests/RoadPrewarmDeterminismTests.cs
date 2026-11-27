// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Landmarks;
using Carbon.Maps.Props;
using Carbon.Maps.Roads;
using Carbon.Maps.Streaming;
using Carbon.Maps.Topography;
using FluentAssertions;
using Xunit;

namespace Carbon.Maps.Tests;

/// <summary>
/// ADR-045 D3, D5, D9, D10: road sites planned in the background, in any order and on any thread, give exactly the
/// roads planned on demand; the background work is capped, drops what no one wants, takes no world lock, and stops
/// cleanly. Runs in the generation group.
/// </summary>
[Collection(GenerationAllocationGroup.Name)]
public sealed class RoadPrewarmDeterminismTests
{
    private const ulong Seed = 17175UL;
    private const float TileSize = 32f;
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(60);

    [Fact]
    public void Prewarm_ConcurrentBuildsInShuffledOrder_MatchTheRoadsPlannedOnDemand()
    {
        // D3: a 6 x 6 site area. One network plans it on demand, the other in the background, sub-areas in shuffled order.
        int size = PointOfInterestResolver.SiteSize;
        (int MinX, int MinY, int MaxX, int MaxY) area = (-3 * size, -3 * size, (3 * size) - 1, (3 * size) - 1);
        ProceduralWorldGenerator onDemand = new(Seed) { RoadPrewarm = new RoadPrewarmOptions(Workers: 0) };
        ProceduralWorldGenerator background = new(Seed) { RoadPrewarm = new RoadPrewarmOptions(Workers: 4) };

        List<(int, int, int, int)> parts = [];
        for (int y = -3; y < 3; y++)
        {
            for (int x = -3; x < 3; x++)
            {
                parts.Add((x * size, y * size, ((x + 1) * size) - 1, ((y + 1) * size) - 1));
            }
        }

        foreach ((int x0, int y0, int x1, int y1) in parts.OrderBy(_ => Random.Shared.Next()))
        {
            background.PrewarmRoads(x0, y0, x1, y1, stream: 1UL);
        }

        WaitForIdle(background);
        background.Roads.BackgroundSiteBuilds.Should().BeGreaterThan(10, "the area was planned in the background");

        Describe(background.Roads, area).Should().Equal(Describe(onDemand.Roads, area),
            "background planning in any order gives the same road links and ramps as planning on demand");
    }

    [Fact]
    public void UpdateAroundPosition_PrewarmOnAndOff_GeneratesIdenticalChunks()
    {
        // D3: the same walk with the prewarm on and off streams byte-identical chunks (tiles, roads, bridges, props).
        ChunkWorld2D withPrewarm = new(new ProceduralWorldGenerator(Seed), TileSize);
        ChunkWorld2D without = new(new ProceduralWorldGenerator(Seed) { RoadPrewarm = new RoadPrewarmOptions(Workers: 0) }, TileSize);
        Vector2 position = new((withPrewarm.SpawnTile.X + 0.5f) * TileSize, (withPrewarm.SpawnTile.Y + 0.5f) * TileSize);

        for (int step = 0; step < 10; step++)
        {
            withPrewarm.UpdateAroundPosition(position, chunkRadius: 2, evictionRadius: 8, stream: 1UL);
            without.UpdateAroundPosition(position, chunkRadius: 2, evictionRadius: 8, stream: 1UL);
            position.X += Chunk2D.Size * TileSize;
        }

        WaitForIdle(withPrewarm.Generator);
        withPrewarm.Generator.Roads.BackgroundSiteBuilds.Should().BeGreaterThan(0, "the prewarm planned road sites");
        foreach (Chunk2D chunk in without.ActiveChunks)
        {
            withPrewarm.TryGetChunk(chunk.ChunkX, chunk.ChunkY, out Chunk2D? twin).Should().BeTrue();
            Fingerprint(twin!).Should().Be(Fingerprint(chunk), $"chunk ({chunk.ChunkX}, {chunk.ChunkY}) is the same either way");
        }
    }

    [Fact]
    public void Prewarm_ManySites_RunsAtMostTheConfiguredWorkersAtOnce()
    {
        // D5.
        ProceduralWorldGenerator generator = new(Seed + 1) { RoadPrewarm = new RoadPrewarmOptions(Workers: 2) };
        int size = PointOfInterestResolver.SiteSize;
        generator.PrewarmRoads(-4 * size, -4 * size, (4 * size) - 1, (4 * size) - 1, stream: 1UL);
        WaitForIdle(generator);

        generator.Roads.BackgroundSiteBuilds.Should().BeGreaterThan(20);
        generator.Roads.PeakWorkers.Should().BeInRange(1, 2, "no more than the configured number of builds run at once");
    }

    [Fact]
    public void Prewarm_ThePlayerMovedAway_DropsTheSitesNoLongerWanted()
    {
        // D5: one worker, a large area, then the same stream wants an area far away: most of the first is never built.
        ProceduralWorldGenerator generator = new(Seed + 2) { RoadPrewarm = new RoadPrewarmOptions(Workers: 1) };
        int size = PointOfInterestResolver.SiteSize;
        generator.PrewarmRoads(-5 * size, -5 * size, (5 * size) - 1, (5 * size) - 1, stream: 1UL);
        int far = 200 * size;
        generator.PrewarmRoads(far, far, far + size - 1, far + size - 1, stream: 1UL);
        WaitForIdle(generator);

        int firstAreaSites = (10 + 4) * (10 + 4); // the query margin adds 2 sites on each side
        int farSites = (1 + 4) * (1 + 4);
        generator.Roads.BackgroundSiteBuilds.Should().BeLessThan((firstAreaSites / 2) + farSites,
            "sites queued for the old area were dropped once nobody wanted them");
    }

    [Fact]
    public void ReleaseStream_ThePlayerLeft_DropsTheSitesItHadQueued()
    {
        // D5: once the only stream wanting them is released, its queued sites are dropped before they start.
        ProceduralWorldGenerator generator = new(Seed + 5) { RoadPrewarm = new RoadPrewarmOptions(Workers: 1) };
        int size = PointOfInterestResolver.SiteSize;
        generator.PrewarmRoads(-5 * size, -5 * size, (5 * size) - 1, (5 * size) - 1, stream: 1UL);
        generator.ReleaseStream(1UL);
        WaitForIdle(generator);

        generator.Roads.BackgroundSiteBuilds.Should().BeLessThan((10 + 4) * (10 + 4) / 2, "nobody wants the queued sites any more");
    }

    [Fact]
    public void PrewarmRoads_WhileTheWorldGateIsHeld_StillCompletesItsBuilds()
    {
        // D9: a background road build takes no world lock.
        ChunkWorld2D world = new(new ProceduralWorldGenerator(Seed + 3), TileSize);
        int size = PointOfInterestResolver.SiteSize;
        bool finished;
        lock (world.Gate)
        {
            world.Generator.PrewarmRoads(-size, -size, size - 1, size - 1, stream: 1UL);
            finished = SpinWait.SpinUntil(() => world.Generator.Roads.PendingBuilds == 0, s_timeout);
        }

        finished.Should().BeTrue("the road builds completed while this thread held the world gate");
        world.Generator.Roads.BackgroundSiteBuilds.Should().BeGreaterThan(0);
    }

    [Fact]
    public void StopBackgroundBuilds_WithRoadSitesQueued_DropsThemAndLeavesNoUnobservedException()
    {
        // D10.
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
            ProceduralWorldGenerator generator = new(Seed + 4) { RoadPrewarm = new RoadPrewarmOptions(Workers: 1) };
            int size = PointOfInterestResolver.SiteSize;
            generator.PrewarmRoads(-6 * size, -6 * size, (6 * size) - 1, (6 * size) - 1, stream: 1UL);
            generator.Roads.PendingBuilds.Should().BeGreaterThan(0);

            generator.StopBackgroundBuilds(s_timeout).Should().BeTrue("every worker finished");
            generator.Roads.PendingBuilds.Should().Be(0);
            generator.Roads.BackgroundSiteBuilds.Should().BeLessThan(16 * 16, "queued sites were dropped");

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            lock (unobserved)
            {
                unobserved.Should().BeEmpty("every background task was observed");
            }
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }
    }

    private static void WaitForIdle(ProceduralWorldGenerator generator) =>
        SpinWait.SpinUntil(() => generator.Roads.PendingBuilds == 0 && generator.Rivers.PendingBuilds == 0, s_timeout)
            .Should().BeTrue("the background builds finish");

    // Every link (points, level, bounds, bridge) and every ramp in the area, in query order.
    private static List<string> Describe(RoadNetwork roads, (int MinX, int MinY, int MaxX, int MaxY) area)
    {
        List<RoadLink> links = [];
        roads.Query(area.MinX, area.MinY, area.MaxX, area.MaxY, links);
        List<string> lines = [.. links
            .Select(l => $"{l.Level}|{l.IsBridge}|{l.MinX},{l.MinY},{l.MaxX},{l.MaxY}|{string.Join(',', l.Points)}")
            .Order(StringComparer.Ordinal)];
        for (int y = area.MinY; y <= area.MaxY; y += 7)
        {
            for (int x = area.MinX; x <= area.MaxX; x += 7)
            {
                if (roads.TryGetRoadRamp(x, y, out RampDirection ramp))
                {
                    lines.Add($"ramp {x},{y} {ramp}");
                }
            }
        }

        return lines;
    }

    private static string Fingerprint(Chunk2D chunk)
    {
        System.Text.StringBuilder text = new();
        for (int y = 0; y < Chunk2D.Size; y++)
        {
            for (int x = 0; x < Chunk2D.Size; x++)
            {
                _ = text.Append(chunk.GetTile(x, y)).Append(chunk.IsRoad(x, y) ? 'r' : '-').Append(chunk.IsBridge(x, y) ? 'b' : '-');
            }
        }

        foreach (MapProp prop in chunk.Props)
        {
            _ = text.Append(prop);
        }

        return text.ToString();
    }
}
