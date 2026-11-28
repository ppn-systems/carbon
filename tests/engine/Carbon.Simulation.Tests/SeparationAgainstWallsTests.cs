// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Physics.Obstacles;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.World;
using FluentAssertions;
using Xunit;

namespace Carbon.Simulation.Tests;

/// <summary>
/// DET-3: entity separation runs after movement has resolved bodies against the world. It must not push a body back
/// into a wall.
/// </summary>
public sealed class SeparationAgainstWallsTests
{
    private const float Radius = 5f;
    private const float WallLeft = 32f; // tile (1, 0) is solid: X in [32, 64)

    [Fact]
    public void Tick_BodyAgainstAWallOverlappedFromTheOpenSide_IsPushedIntoTheWall()
    {
        WorldSimulation world = new(tickRate: 20);
        TileObstacleGrid grid = new(10, 10, 32f);
        grid.SetSolid(1, 0, true);
        world.ObstacleGrid = grid;
        Entity againstWall = world.SpawnEntity(new Vector2(WallLeft - Radius - 0.1f, 16f), Radius);
        _ = world.SpawnEntity(new Vector2(WallLeft - Radius - 7f, 16f), Radius); // overlaps it by 3 units

        _ = world.Update(0.05);

        // Characterisation: separation moves each body half the overlap, so the one at the wall ends 1.4 inside it.
        float right = world.Entities.GetTransform(againstWall).Position.X + Radius;
        right.Should().BeGreaterThan(WallLeft + 1f);
    }

    [Fact]
    public void Tick_TwoMoversPressedTogetherAtAWall_EndEveryTickInsideIt()
    {
        // Players and monsters all carry a velocity, so movement pushes the body out of the wall at the start of every tick;
        // separation then pushes it back in, and the replicated position after the tick is inside the wall again.
        WorldSimulation world = new(tickRate: 20);
        TileObstacleGrid grid = new(10, 10, 32f);
        grid.SetSolid(1, 0, true);
        world.ObstacleGrid = grid;
        Entity againstWall = world.SpawnEntity(new Vector2(WallLeft - Radius - 0.1f, 16f), Radius);
        Entity pusher = world.SpawnEntity(new Vector2(WallLeft - Radius - 7f, 16f), Radius);
        world.Entities.SetVelocity(againstWall, new Velocity2D(Vector2.Zero));
        world.Entities.SetVelocity(pusher, new Velocity2D(new Vector2(60f, 0f)));

        int ticksInsideTheWall = 0;
        for (int t = 0; t < 10; t++)
        {
            _ = world.Update(0.05);
            ticksInsideTheWall += world.Entities.GetTransform(againstWall).Position.X + Radius > WallLeft + 0.01f ? 1 : 0;
        }

        // Characterisation: every one of the 10 ticks ends with the body inside the wall.
        ticksInsideTheWall.Should().Be(10);
    }
}
