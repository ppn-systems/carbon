// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Physics.Shapes;
using Carbon.Simulation.ECS;
using Carbon.Simulation.World;
using FluentAssertions;
using Xunit;

namespace Carbon.Simulation.Tests;

/// <summary>
/// HP-11: crowded spatial queries. A query that finds more entities than a buffer holds must still return the nearest.
/// </summary>
public sealed class SpatialQueryOverflowTests
{
    [Fact]
    public void OverlapCircle_MoreOverlapsThanTheResultsBuffer_ResultsMissTheNearest()
    {
        WorldSimulation world = new();
        for (int i = 0; i < 40; i++)
        {
            float angle = i * (MathF.Tau / 40f);
            _ = world.SpawnEntity(new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 80f, radius: 2f);
        }

        Entity nearest = world.SpawnEntity(new Vector2(10f, 0f), radius: 2f);
        Span<Entity> results = new Entity[16];

        world.OverlapCircle(Vector2.Zero, 100f, results, out int count);

        // Characterisation: the buffer fills in grid order, so the nearest overlap is dropped.
        count.Should().Be(16);
        results[..count].ToArray().Should().NotContain(nearest);
    }

    [Fact]
    public void OverlapCircle_CrowdInTheQueriedCellsBeyondTheCandidateScratch_MissesTheOverlap()
    {
        WorldSimulation world = new();
        SpawnCrowd(world, new Vector2(-60f, -60f), 600);
        Entity target = world.SpawnEntity(new Vector2(5f, 0f), radius: 2f);
        Span<Entity> results = new Entity[4];

        world.OverlapCircle(Vector2.Zero, 20f, results, out int count);

        // Characterisation: 600 non-overlapping entities in an earlier cell fill the 512-entry candidate scratch.
        count.Should().Be(0);
        _ = target;
    }

    [Fact]
    public void RaycastEntities_CrowdInTheQueriedCellsBeyondTheCandidateScratch_MissesTheHit()
    {
        WorldSimulation world = new();
        SpawnCrowd(world, new Vector2(-20f, -20f), 600);
        Entity target = world.SpawnEntity(new Vector2(150f, 0f), radius: 5f);
        Ray2D ray = new(Vector2.Zero, Vector2.UnitX, 200f);

        bool hit = world.RaycastEntities(in ray, out Entity hitEntity, out _);

        // Characterisation: the crowd off the ray fills the candidate scratch, so the entity on it is never tested.
        hit.Should().BeFalse();
        hitEntity.Should().NotBe(target);
    }

    private static void SpawnCrowd(WorldSimulation world, Vector2 at, int count)
    {
        for (int i = 0; i < count; i++)
        {
            _ = world.SpawnEntity(at, radius: 1f);
        }
    }
}
