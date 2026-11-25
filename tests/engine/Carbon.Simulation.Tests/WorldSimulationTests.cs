// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using System.Threading.Tasks;
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

    [Fact]
    public void EnqueueCommand_MoveInputWithWorldPhysics_AppliesTerrainSpeedMultiplierOnTick()
    {
        WorldSimulation world = new(tickRate: 20)
        {
            WorldPhysics = new ScaledSpeedWorldPhysics(multiplier: 0.5f)
        };

        Entity player = world.SpawnEntity(new Vector2(0f, 0f), radius: 5f);
        world.Entities.SetVelocity(player, new Velocity2D(0f, 0f, drag: 0f));
        world.EnqueueCommand(SimulationCommand.MoveInput(player, new Vector2(1f, 0f), baseSpeed: 200f));

        world.PendingCommandCount.Should().Be(1);
        _ = world.Update(0.05);

        world.PendingCommandCount.Should().Be(0);
        Vector2 pos = world.Entities.GetTransform(player).Position;
        pos.X.Should().BeApproximately(5f, 0.001f); // 200 * 0.5 * 0.05 = 5
    }

    [Fact]
    public void EnqueueCommand_ConcurrentProducers_ProcessesAllCommandsSafelyOnTick()
    {
        WorldSimulation world = new(tickRate: 20);
        const int count = 32;
        Entity[] entities = new Entity[count];
        for (int i = 0; i < count; i++)
        {
            entities[i] = world.SpawnEntity(new Vector2(i * 100f, 0f), radius: 5f);
        }

        _ = Parallel.For(0, count, i =>
            world.EnqueueCommand(SimulationCommand.Teleport(entities[i], new Vector2(i * 100f, 42f))));

        _ = world.Update(0.05);

        world.PendingCommandCount.Should().Be(0);
        for (int i = 0; i < count; i++)
        {
            world.Entities.GetTransform(entities[i]).Position.Y.Should().BeApproximately(42f, 0.001f);
        }
    }

    [Fact]
    public void Movement_HighSpeedSprintAgainstThinWall_DoesNotTunnelThroughObstacle()
    {
        WorldSimulation world = new(tickRate: 20);
        TileObstacleGrid grid = new(10, 10, 32f);
        grid.SetSolid(1, 0, true); // 1-tile wall at X: [32, 64]
        world.WorldPhysics = grid;

        // Extreme velocity (2000 u/s * 0.05s = 100u displacement in a single tick) would jump past [32, 64] without CCD
        Entity player = world.SpawnEntity(new Vector2(16f, 16f), radius: 5f);
        world.Entities.SetVelocity(player, new Velocity2D(2000f, 0f));

        _ = world.Update(0.05);

        Vector2 pos = world.Entities.GetTransform(player).Position;
        pos.X.Should().BeLessThanOrEqualTo(27.01f);
    }

    private sealed class ScaledSpeedWorldPhysics(float multiplier) : IWorldPhysics2D
    {
        public float GetMoveSpeedMultiplier(Vector2 worldPosition) => multiplier;

        public bool ResolveCircleCollision(ref Vector2 position, float radius, out Vector2 pushNormal)
        {
            pushNormal = Vector2.Zero;
            return false;
        }
    }
}
