// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Carbon.Physics.Obstacles;
using Carbon.Physics.Raycast;
using Carbon.Physics.Shapes;
using Carbon.Simulation.Clock;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.ECS.Systems;
using Carbon.Simulation.Events;
using Carbon.Simulation.Networking;
using Carbon.Simulation.Spatial;
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

    [Fact]
    public void RegisterSystem_MultiplePhases_ExecutesInDeterministicPhaseOrder()
    {
        WorldSimulation world = new(tickRate: 20);
        List<SimulationPhase> order = [];
        world.RegisterSystem(new RecordingSystem(SimulationPhase.PostUpdate, order));
        world.RegisterSystem(new RecordingSystem(SimulationPhase.PreUpdate, order));
        world.RegisterSystem(new RecordingSystem(SimulationPhase.Physics, order));
        world.RegisterSystem(new RecordingSystem(SimulationPhase.Movement, order));

        _ = world.Update(0.05);

        order.Should().Equal(
            SimulationPhase.PreUpdate,
            SimulationPhase.Movement,
            SimulationPhase.Physics,
            SimulationPhase.PostUpdate);
    }

    [Fact]
    public void EntityCollision_TriggerOverlap_EmitsEventWithoutDisplacementAndClearsOnNextUpdate()
    {
        WorldSimulation world = new(tickRate: 20);
        Entity zone = world.SpawnEntity(new Vector2(0f, 0f), radius: 20f);
        world.Entities.SetCollider(zone, Collider2D.CreateCircle(20f, isTrigger: true));

        Entity visitor = world.SpawnEntity(new Vector2(10f, 0f), radius: 5f);

        _ = world.Update(0.05);

        ReadOnlySpan<TriggerOverlapEvent> events = world.ReadEvents<TriggerOverlapEvent>();
        events.Length.Should().Be(1);
        events[0].TriggerEntity.Should().Be(zone);
        events[0].OtherEntity.Should().Be(visitor);
        world.Entities.GetTransform(visitor).Position.Should().Be(new Vector2(10f, 0f));

        // Move visitor outside and advance another frame: old events must clear
        world.EnqueueCommand(SimulationCommand.Teleport(visitor, new Vector2(100f, 0f)));
        _ = world.Update(0.05);
        world.ReadEvents<TriggerOverlapEvent>().Length.Should().Be(0);
    }

    [Fact]
    public void EntityCollision_AabbAndCircleColliders_SeparateOverlappingEntities()
    {
        WorldSimulation world = new(tickRate: 20, cellSize: 64f);
        Entity boxEntity = world.SpawnEntity(new Vector2(0f, 0f));
        world.Entities.SetCollider(boxEntity, Collider2D.CreateAabb(AABB2D.FromCenterSize(Vector2.Zero, new Vector2(20f, 20f))));

        Entity circleEntity = world.SpawnEntity(new Vector2(14f, 0f), radius: 6f);

        _ = world.Update(0.05);

        float boxRight = world.Entities.GetTransform(boxEntity).Position.X + 10f;
        float circleLeft = world.Entities.GetTransform(circleEntity).Position.X - 6f;
        circleLeft.Should().BeGreaterThanOrEqualTo(boxRight - 0.01f);

        // Two AABB colliders overlapping
        Entity boxA = world.SpawnEntity(new Vector2(200f, 0f));
        Entity boxB = world.SpawnEntity(new Vector2(214f, 0f));
        AABB2D localBox = AABB2D.FromCenterSize(Vector2.Zero, new Vector2(20f, 20f));
        world.Entities.SetCollider(boxA, Collider2D.CreateAabb(in localBox));
        world.Entities.SetCollider(boxB, Collider2D.CreateAabb(in localBox));

        _ = world.Update(0.05);
        float gap = world.Entities.GetTransform(boxB).Position.X - world.Entities.GetTransform(boxA).Position.X;
        gap.Should().BeGreaterThanOrEqualTo(19.99f);
    }

    [Fact]
    public void GetInterpolatedPosition_BetweenFixedTicks_InterpolatesAndTeleportSnaps()
    {
        WorldSimulation world = new(tickRate: 20); // 50ms per tick
        Entity e = world.SpawnEntity(new Vector2(0f, 0f), radius: 5f);
        world.Entities.SetVelocity(e, new Velocity2D(200f, 0f));

        // 0.075s = 1 tick executed (pos moves 0 -> 10), 0.025s remaining => InterpolationAlpha = 0.5
        int ticks = world.Update(0.075);
        ticks.Should().Be(1);
        world.Clock.InterpolationAlpha.Should().BeApproximately(0.5f, 0.001f);
        world.GetInterpolatedPosition(e).X.Should().BeApproximately(5f, 0.01f);

        // Teleport inside tick snaps both PreviousTransform2D and Transform2D
        world.EnqueueCommand(SimulationCommand.Teleport(e, new Vector2(500f, 0f)));
        world.Entities.SetVelocity(e, new Velocity2D(0f, 0f));
        _ = world.Update(0.05);
        world.GetInterpolatedPosition(e, alpha: 0f).X.Should().BeApproximately(500f, 0.001f);
        world.GetInterpolatedPosition(e, alpha: 1f).X.Should().BeApproximately(500f, 0.001f);
    }

    [Fact]
    public void NetworkIdentityAndDirtyFlags_TracksSpawnMoveDespawnAndAoiDiff()
    {
        WorldSimulation world = new(tickRate: 20, cellSize: 32f);
        Entity e1 = world.SpawnNetworkEntity(9001UL, new Vector2(16f, 16f), radius: 5f);
        world.Entities.SetVelocity(e1, new Velocity2D(0f, 0f));
        Entity e2 = world.SpawnNetworkEntity(9002UL, new Vector2(80f, 16f), radius: 5f);

        world.TryGetEntityByNetworkId(9001UL, out Entity resolved).Should().BeTrue();
        resolved.Should().Be(e1);
        world.GetDirtyFlags(e1).HasFlag(EntityDirtyMask.Spawned).Should().BeTrue();
        world.ClearDirtyFlags(e1);
        world.GetDirtyFlags(e1).Should().Be(EntityDirtyMask.None);

        world.EnqueueCommand(SimulationCommand.Velocity(e1, new Vector2(200f, 0f)));
        _ = world.Update(0.05);
        world.GetDirtyFlags(e1).HasFlag(EntityDirtyMask.Velocity | EntityDirtyMask.Transform).Should().BeTrue();

        // AoI diff when observer moves from cell (0,0) to (1,0) with radius 1: cell (2,0) enters (contains e2)
        Span<int> entered = stackalloc int[16];
        Span<int> exited = stackalloc int[16];
        world.SpatialGrid.QueryAoiDiff(new CellCoord(0, 0), new CellCoord(1, 0), cellRadius: 1, entered, out int enteredCount, exited, out int exitedCount);
        entered[..enteredCount].ToArray().Should().Contain(e2.Id);
        exitedCount.Should().Be(0);

        world.DespawnEntity(e1);
        world.TryGetEntityByNetworkId(9001UL, out _).Should().BeFalse();
        ReadOnlySpan<EntityDespawnedEvent> despawns = world.ReadEvents<EntityDespawnedEvent>();
        despawns.Length.Should().Be(1);
        despawns[0].NetworkId.Should().Be(9001UL);
    }

    [Fact]
    public void RaycastAndOverlapCircle_FindsClosestEntityAndRespectsStaticObstacles()
    {
        WorldSimulation world = new(tickRate: 20, cellSize: 32f);
        TileObstacleGrid grid = new(10, 10, 32f);
        grid.SetSolid(4, 0, true); // Wall at X: [128..160]
        world.WorldPhysics = grid;

        Entity shooter = world.SpawnEntity(new Vector2(10f, 16f), radius: 5f);
        Entity nearTarget = world.SpawnEntity(new Vector2(60f, 16f), radius: 8f);
        Entity behindWall = world.SpawnEntity(new Vector2(180f, 16f), radius: 8f);

        Ray2D ray = new(new Vector2(10f, 16f), Vector2.UnitX, 300f);
        world.RaycastWorld(ray, out WorldRaycastHit hit, ignoreEntity: shooter).Should().BeTrue();
        hit.Entity.Should().Be(nearTarget);
        hit.Distance.Should().BeApproximately(42f, 0.05f); // 60 - 8 - 10

        // Ray ignoring nearTarget hits the static wall before reaching behindWall
        Ray2D wallRay = new(new Vector2(80f, 16f), Vector2.UnitX, 200f);
        world.RaycastWorld(wallRay, out WorldRaycastHit wallHit).Should().BeTrue();
        wallHit.Entity.Should().Be(Entity.Null);
        wallHit.Point.X.Should().BeApproximately(128f, 0.05f);

        Span<Entity> overlaps = stackalloc Entity[8];
        world.OverlapCircle(new Vector2(55f, 16f), 15f, overlaps, out int overlapCount);
        overlapCount.Should().Be(1);
        overlaps[0].Should().Be(nearTarget);
        world.Entities.IsAlive(behindWall).Should().BeTrue();
    }

    private sealed class RecordingSystem(SimulationPhase phase, List<SimulationPhase> log) : ISimulationSystem
    {
        public SimulationPhase Phase => phase;

        public void Update(WorldSimulation world, FixedTickArgs tick) => log.Add(phase);
    }

    private sealed class ScaledSpeedWorldPhysics(float multiplier) : IWorldPhysics2D
    {
        public float GetMoveSpeedMultiplier(Vector2 worldPosition) => multiplier;

        public bool ResolveCircleCollision(ref Vector2 position, float radius, out Vector2 pushNormal)
        {
            pushNormal = Vector2.Zero;
            return false;
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

    [Fact]
    public void EntityAttachmentStore_AutomaticallyClears_WhenEntityDestroyed()
    {
        EntityManager manager = new(16);
        Entity e1 = manager.CreateEntity();
        EntityAttachmentStore<List<int>> store = manager.GetAttachmentStore<List<int>>();

        List<int> attached = store.GetOrAdd(e1, static () => [42]);
        attached.Should().ContainSingle().Which.Should().Be(42);
        store.Has(e1).Should().BeTrue();

        manager.DestroyEntity(e1).Should().BeTrue();
        store.Has(e1).Should().BeFalse();
        store.TryGet(e1, out _).Should().BeFalse();

        // Recycled slot with higher generation must not expose old entity's attachment
        Entity e2 = manager.CreateEntity();
        store.Has(e2).Should().BeFalse();
    }

    [Fact]
    public void SpatialHashGrid2D_MultiCellLargeAabb_QueriesWithoutDuplicates()
    {
        SpatialHashGrid2D grid = new(cellSize: 32f);
        AABB2D giantBounds = new(new Vector2(10f, 10f), new Vector2(80f, 80f)); // Spans (0,0)..(2,2) = 9 cells
        grid.Insert(1, in giantBounds);

        Span<int> buffer = stackalloc int[16];
        grid.QueryRegion(new CellCoord(0, 0), new CellCoord(2, 2), buffer, out int count);
        count.Should().Be(1);
        buffer[0].Should().Be(1);

        // Moving slightly while still occupying all 9 cells returns false (no cell set change)
        grid.Update(1, new AABB2D(new Vector2(12f, 12f), new Vector2(78f, 78f))).Should().BeFalse();

        // QueryAoiDiff should not mark an already-visible multi-cell entity as newly entered
        Span<int> entered = stackalloc int[16];
        Span<int> exited = stackalloc int[16];
        grid.QueryAoiDiff(new CellCoord(0, 0), new CellCoord(1, 0), cellRadius: 1, entered, out int enteredCount, exited, out int exitedCount);
        enteredCount.Should().Be(0);
        exitedCount.Should().Be(0);
    }

    [Fact]
    public void GetInterpolatedTransform_AndMovementAabb_AndLargeEntityCount_WorkCorrectly()
    {
        TileObstacleGrid obstacles = new(width: 8, height: 8, tileSize: 32f);
        obstacles.SetSolid(2, 1, true); // [64..96, 32..64]
        WorldSimulation world = new(tickRate: 20)
        {
            WorldPhysics = obstacles
        };

        Entity aabbMover = world.SpawnEntity(new Vector2(54f, 48f), radius: 8f);
        world.Entities.SetCollider(aabbMover, Collider2D.CreateAabb(AABB2D.FromCenterSize(Vector2.Zero, new Vector2(16f, 16f))));
        world.Entities.SetVelocity(aabbMover, new Velocity2D(200f, 0f));

        // Wrap shortest arc from +170deg to -170deg across PI boundary
        Entity rotEntity = world.SpawnEntity(Vector2.Zero, radius: 1f);
        float nearPlusPi = MathF.PI - 0.1f;
        float nearMinusPi = -MathF.PI + 0.1f;
        world.Entities.SetPreviousTransform(rotEntity, new PreviousTransform2D(Vector2.Zero, nearPlusPi));
        world.Entities.SetTransform(rotEntity, new Transform2D(Vector2.Zero, nearMinusPi));

        Transform2D mid = world.GetInterpolatedTransform(rotEntity, alpha: 0.5f);
        MathF.Abs(MathF.Abs(mid.Rotation) - MathF.PI).Should().BeLessThan(0.05f);

        // Advance one tick so AABB mover collides cleanly with tile [64..96] -> Max.X <= 64 => X <= 56
        _ = world.Update(0.05);
        world.Entities.GetTransform(aabbMover).Position.X.Should().BeLessThanOrEqualTo(56.01f);

        // Spawn > 256 entities to trigger ArrayPool<int>.Shared fallback in EntityCollisionSystem without overflow
        for (int i = 0; i < 270; i++)
        {
            int gx = i % 10;
            int gy = i / 10;
            _ = world.SpawnEntity(new Vector2(200f + (gx * 5f), 200f + (gy * 5f)), radius: 2f);
        }

        _ = world.Update(0.05);
        world.SpatialGrid.EntityCount.Should().BeGreaterThan(260);
    }
}
