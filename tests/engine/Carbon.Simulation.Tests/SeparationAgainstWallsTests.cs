// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Physics.Raycast;
using Carbon.Physics.Shapes;
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
    public void Tick_BodyAgainstAWallOverlappedFromTheOpenSide_StaysOutOfTheWall()
    {
        WorldSimulation world = new(tickRate: 20);
        TileObstacleGrid grid = new(10, 10, 32f);
        grid.SetSolid(1, 0, true);
        world.ObstacleGrid = grid;
        Entity againstWall = world.SpawnEntity(new Vector2(WallLeft - Radius - 0.1f, 16f), Radius);
        _ = world.SpawnEntity(new Vector2(WallLeft - Radius - 7f, 16f), Radius); // overlaps it by 3 units

        _ = world.Update(0.05);

        float right = world.Entities.GetTransform(againstWall).Position.X + Radius;
        right.Should().BeLessThanOrEqualTo(WallLeft + 0.01f, "the wall wins over the separation push");
    }

    [Fact]
    public void Tick_TwoMoversPressedTogetherAtAWall_NeverEndATickInsideIt()
    {
        // Players and monsters all carry a velocity, so movement pushes the body out of the wall at the start of every tick;
        // separation must not then push it back in, or the replicated position after the tick is inside the wall.
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

        ticksInsideTheWall.Should().Be(0);
    }

    [Fact]
    public void Tick_WallBodyIsTheSecondOfThePair_StaysOutOfTheWall()
    {
        // The pair is visited in slot order; here the body at the wall is the second one (B).
        WorldSimulation world = new(tickRate: 20);
        TileObstacleGrid grid = new(10, 10, 32f);
        grid.SetSolid(1, 0, true);
        world.ObstacleGrid = grid;
        _ = world.SpawnEntity(new Vector2(WallLeft - Radius - 7f, 16f), Radius);
        Entity againstWall = world.SpawnEntity(new Vector2(WallLeft - Radius - 0.1f, 16f), Radius);

        _ = world.Update(0.05);

        (world.Entities.GetTransform(againstWall).Position.X + Radius).Should().BeLessThanOrEqualTo(WallLeft + 0.01f);
    }

    [Fact]
    public void Tick_SwimmerPushedPastTheShoreLine_UsesItsTerrainMask()
    {
        // A swimmer's mask lets it through deep water; the world resolution after separation must see that mask too.
        WorldSimulation world = new(tickRate: 20) { WorldPhysics = new WaterAtTheRight() };
        Entity swimmer = world.SpawnEntity(new Vector2(WallLeft - Radius - 0.1f, 16f), Radius);
        world.Entities.SetCollider(swimmer, new Collider2D(ColliderKind.Circle, Radius, default, false, 1U, WaterAtTheRight.SwimBit | 1U));
        _ = world.SpawnEntity(new Vector2(WallLeft - Radius - 7f, 16f), Radius);

        _ = world.Update(0.05);

        (world.Entities.GetTransform(swimmer).Position.X + Radius).Should().BeGreaterThan(WallLeft + 1f, "deep water does not block a swimmer");
    }

    // Deep water from x = 32 on: solid to a walker, open to a collider whose mask has SwimBit.
    private sealed class WaterAtTheRight : IWorldPhysics2D
    {
        public const uint SwimBit = 1U << 30;

        public float GetMoveSpeedMultiplier(Vector2 worldPosition) => 1f;

        public bool ResolveCircleCollision(ref Vector2 position, float radius, out Vector2 pushNormal) =>
            this.ResolveCircleCollision(ref position, radius, 0U, out pushNormal);

        public bool ResolveCircleCollision(ref Vector2 position, float radius, uint terrainMask, out Vector2 pushNormal)
        {
            pushNormal = Vector2.Zero;
            if ((terrainMask & SwimBit) != 0 || position.X + radius <= WallLeft)
            {
                return false;
            }

            position.X = WallLeft - radius;
            pushNormal = -Vector2.UnitX;
            return true;
        }

        public bool ResolveAabbCollision(ref Vector2 position, in AABB2D localBox, out Vector2 pushNormal)
        {
            pushNormal = Vector2.Zero;
            return false;
        }

        public bool Raycast(in Ray2D ray, out RaycastHit2D hit)
        {
            hit = RaycastHit2D.None;
            return false;
        }
    }
}
