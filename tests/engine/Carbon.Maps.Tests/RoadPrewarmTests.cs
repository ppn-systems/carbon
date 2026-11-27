// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using System.Threading;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Hydrology;
using Carbon.Maps.Roads;
using Carbon.Maps.Streaming;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Carbon.Maps.Tests;

/// <summary>
/// ADR-045: a player walking into new land must not make the tick plan road sites, or build the river regions their
/// routes need. Measured as builds on the calling thread, not as time. Runs in the generation group, so other tests'
/// background work cannot hold back this one's.
/// </summary>
[Collection(GenerationAllocationGroup.Name)]
public sealed class RoadPrewarmTests(ITestOutputHelper output)
{
    private const float TileSize = 32f;
    private const int Steps = 12;
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(60);

    [Fact]
    public void UpdateAroundPosition_WalkingIntoNewLand_BuildsRoadSitesOnTheCallingThread()
    {
        // Characterisation: roads are planned on the thread that streams the chunk in.
        ProceduralWorldGenerator generator = new(9256UL);
        ChunkWorld2D world = new(generator, TileSize);
        Vector2 position = new((world.SpawnTile.X + 0.5f) * TileSize, (world.SpawnTile.Y + 0.5f) * TileSize);
        Step(world, position);

        RoadNetwork roads = generator.Roads;
        RiverNetwork rivers = generator.Rivers;
        int roadsBefore = roads.SiteBuildsOnThisThread;
        int regionsBefore = rivers.RegionBuildsOnThisThread;
        for (int i = 0; i < Steps; i++)
        {
            position.X += Chunk2D.Size * TileSize;
            Step(world, position);
        }

        int roadBuilds = roads.SiteBuildsOnThisThread - roadsBefore;
        int regionBuilds = rivers.RegionBuildsOnThisThread - regionsBefore;
        output.WriteLine($"{Steps} steps east: {roadBuilds} road sites and {regionBuilds} river regions built on the calling thread");
        roadBuilds.Should().BeGreaterThan(0, "the tick plans the road sites it walks into");
    }

    // One streaming step with the server's radii, then the background builds it started run to completion, as they
    // would during the ticks before the player reaches the next chunk.
    private static void Step(ChunkWorld2D world, Vector2 position)
    {
        world.UpdateAroundPosition(position, chunkRadius: 2, evictionRadius: 8, stream: 1UL);
        SpinWait.SpinUntil(() => world.Generator.Rivers.PendingBuilds == 0, s_timeout).Should().BeTrue("background builds finish");
    }
}
