// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Physics.Obstacles;
using Carbon.Physics.Raycast;
using Carbon.Physics.Shapes;
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

    [Fact]
    public void Movement_WithObstacleGrid_StopsEntityFromEnteringSolidTiles()
    {
        WorldSimulation world = new(tickRate: 20);
        TileObstacleGrid grid = new(10, 10, 32f);
        grid.SetSolid(1, 0, true); // Solid wall at X: [32, 64], Y: [0, 32]
        world.ObstacleGrid = grid;

        // Player starting at (20, 16) with radius 5, moving right towards the wall at 200 units/s
        Entity player = world.SpawnEntity(new Vector2(20f, 16f), radius: 5f);
        world.Entities.SetVelocity(player, new Velocity2D(new Vector2(200f, 0f)));

        // Advance 0.1s (2 ticks at 20Hz -> without obstacle, position would be 20 + 20 = 40, inside the solid wall)
        _ = world.Update(0.1);

        Vector2 pos = world.Entities.GetTransform(player).Position;
        // With collision, position X must not penetrate X=32 (so pos.X <= 27)
        pos.X.Should().BeLessThanOrEqualTo(27.01f);
    }

    [Fact]
    public void WorldSimulation_Raycast_HitsObstacleGrid()
    {
        WorldSimulation world = new();
        TileObstacleGrid grid = new(10, 10, 32f);
        grid.SetSolid(2, 0, true); // Wall at X: [64, 96]
        world.ObstacleGrid = grid;

        Ray2D ray = new(new Vector2(0f, 16f), new Vector2(1f, 0f), 100f);
        bool hasHit = world.Raycast(ray, out RaycastHit2D hit);

        hasHit.Should().BeTrue();
        hit.HasHit.Should().BeTrue();
        hit.Point.X.Should().BeApproximately(64f, 0.01f);
    }
}
