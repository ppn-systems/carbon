// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Physics.Shapes;
using Carbon.Simulation.ECS;
using Carbon.Simulation.World;
using Carbon.Testing;
using FluentAssertions;
using Xunit;

namespace Carbon.Simulation.Tests;

/// <summary>
/// HP-11: crowded spatial queries. A query that finds more entities than a buffer holds must still return the nearest.
/// </summary>
public sealed class SpatialQueryOverflowTests
{
    [Fact]
    public void OverlapCircle_MoreOverlapsThanTheResultsBuffer_KeepsTheNearest()
    {
        WorldSimulation world = new();
        for (int i = 0; i < 40; i++)
        {
            float angle = i * (MathF.Tau / 40f);
            _ = world.SpawnEntity(new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 80f, radius: 2f);
        }

        Entity[] middle = new Entity[15];
        for (int i = 0; i < middle.Length; i++)
        {
            float angle = i * (MathF.Tau / middle.Length);
            middle[i] = world.SpawnEntity(new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 40f, radius: 2f);
        }

        Entity nearest = world.SpawnEntity(new Vector2(10f, 0f), radius: 2f);
        Span<Entity> results = new Entity[16];

        world.OverlapCircle(Vector2.Zero, 100f, results, out int count);

        count.Should().Be(16);
        results[..count].ToArray().Should().Contain(nearest, "a full buffer keeps the nearest overlaps, not the first found")
            .And.Contain(middle, "the 16 nearest are the one at 10 and the 15 at 40, none of the 40 at 80");
    }

    [Fact]
    public void OverlapCircle_CrowdInTheQueriedCellsBeyondTheCandidateScratch_StillFindsTheOverlap()
    {
        WorldSimulation world = new();
        SpawnCrowd(world, new Vector2(-60f, -60f), 600);
        Entity target = world.SpawnEntity(new Vector2(5f, 0f), radius: 2f);
        Span<Entity> results = new Entity[4];

        world.OverlapCircle(Vector2.Zero, 20f, results, out int count);

        // 600 non-overlapping entities in an earlier cell would fill the 512-entry candidate scratch.
        count.Should().Be(1);
        results[0].Should().Be(target);
    }

    [Fact]
    public void RaycastEntities_CrowdInTheQueriedCellsBeyondTheCandidateScratch_StillHits()
    {
        WorldSimulation world = new();
        SpawnCrowd(world, new Vector2(-20f, -20f), 600);
        Entity target = world.SpawnEntity(new Vector2(150f, 0f), radius: 5f);
        Ray2D ray = new(Vector2.Zero, Vector2.UnitX, 200f);

        bool hit = world.RaycastEntities(in ray, out Entity hitEntity, out _);

        // The crowd off the ray would fill the 512-entry candidate scratch before the entity on it is tested.
        hit.Should().BeTrue();
        hitEntity.Should().Be(target);
    }

    [Fact]
    public void CrowdedQueries_SteadyState_AllocateNothing()
    {
        WorldSimulation world = new();
        SpawnCrowd(world, new Vector2(-20f, -20f), 600);
        _ = world.SpawnEntity(new Vector2(150f, 0f), radius: 5f);
        Entity[] results = new Entity[4];
        Ray2D ray = new(Vector2.Zero, Vector2.UnitX, 200f);

        // Both queries overflow the stack scratch every time, so each one rents and returns a pooled buffer.
        AllocationResult window = AllocationProbe.Measure(200, warmupIterations: 20, i =>
        {
            world.OverlapCircle(new Vector2(-20f, -20f), 4f, results, out _);
            _ = world.RaycastEntities(in ray, out _, out _);
        });

        window.Bytes.Should().Be(0, $"the larger buffer is pooled ({window})");
    }

    private static void SpawnCrowd(WorldSimulation world, Vector2 at, int count)
    {
        for (int i = 0; i < count; i++)
        {
            _ = world.SpawnEntity(at, radius: 1f);
        }
    }
}
