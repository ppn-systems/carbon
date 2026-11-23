// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.World;
using FluentAssertions;
using Xunit;

namespace Carbon.Simulation.Tests;

public sealed class WorldSimulationTests
{
    [Fact]
    public void Movement_ShouldIntegrateVelocityOverFixedTicks()
    {
        WorldSimulation world = new(tickRate: 20);

        Entity player = world.SpawnEntity(new Vector2(0f, 0f), radius: 10f);
        world.Entities.SetVelocity(player, new Velocity2D(new Vector2(100f, 0f)));

        int ticks = world.Update(0.1);

        ticks.Should().Be(2);
        Vector2 finalPos = world.Entities.GetTransform(player).Position;
        finalPos.X.Should().BeApproximately(10f, 0.001f);
        finalPos.Y.Should().Be(0f);
    }

    [Fact]
    public void EntityCollision_WhenEntitiesOverlap_SeparatesThemAutomatically()
    {
        WorldSimulation world = new(tickRate: 20, cellSize: 32f);

        Entity e1 = world.SpawnEntity(new Vector2(0f, 0f), radius: 10f);
        Entity e2 = world.SpawnEntity(new Vector2(10f, 0f), radius: 10f);

        _ = world.Update(0.05);

        Vector2 pos1 = world.Entities.GetTransform(e1).Position;
        Vector2 pos2 = world.Entities.GetTransform(e2).Position;

        float distance = Vector2.Distance(pos1, pos2);
        distance.Should().BeGreaterThanOrEqualTo(19.99f);
        pos1.X.Should().BeLessThan(pos2.X);
    }

    [Fact]
    public void DespawnEntity_RemovesFromWorldAndSpatialGrid()
    {
        WorldSimulation world = new();
        Entity entity = world.SpawnEntity(new Vector2(50f, 50f));

        world.Entities.EntityCount.Should().Be(1);
        world.SpatialGrid.EntityCount.Should().Be(1);

        world.DespawnEntity(entity);

        world.Entities.EntityCount.Should().Be(0);
        world.SpatialGrid.EntityCount.Should().Be(0);
        world.Entities.IsAlive(entity).Should().BeFalse();
    }
}
