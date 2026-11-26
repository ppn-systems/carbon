// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Numerics;
using Carbon.Application.World.Projectiles;
using Carbon.Physics.Obstacles;
using Carbon.Physics.Raycast;
using Carbon.Physics.Shapes;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.World;
using FluentAssertions;
using Xunit;

namespace Carbon.Host.Tests;

public sealed class ProjectileServiceTests
{
    private const uint MonsterLayer = 2U;

    [Fact]
    public void Spawn_ShortLifetime_TracksTriggerProjectileWithMinimumTwoTicks()
    {
        Harness h = new();

        Entity proj = h.Projectiles.Spawn(7UL, 3UL, fromPlayer: true, Vector2.Zero, new Vector2(100f, 0f), 42, 10f, true, lifetimeTicks: 0U);

        h.Projectiles.TryGet(h.FirstNetworkId, out ProjectileState state).Should().BeTrue();
        state.Entity.Should().Be(proj);
        state.RemainingTicks.Should().Be(2U);
        state.VisualArchetype.Should().Be(42);
        Collider2D collider = h.Simulation.Entities.GetCollider(proj);
        collider.IsTrigger.Should().BeTrue();
        collider.Layer.Should().Be(ProjectileService.ProjectileLayer);
    }

    [Fact]
    public void Step_PlayerProjectileSweepsPastTwoMonsters_HitsTheClosestAlongTheSegmentAndDespawns()
    {
        Harness h = new();
        Entity far = h.SpawnMonster(new Vector2(30f, 2f));
        Entity near = h.SpawnMonster(new Vector2(12f, -2f));
        Entity proj = h.Projectiles.Spawn(7UL, 3UL, fromPlayer: true, Vector2.Zero, new Vector2(100f, 0f), 1, 10f, false, 30U);
        h.Move(proj, Vector2.Zero, new Vector2(40f, 0f));

        h.Projectiles.Step();

        h.Hits.MonsterHits.Should().Equal(near);
        h.Hits.MonsterHits.Should().NotContain(far);
        h.Hits.LastShotDirection.Should().Be(Vector2.UnitX);
        h.Projectiles.Count.Should().Be(0);
        h.Simulation.Entities.IsAlive(proj).Should().BeFalse();
    }

    [Fact]
    public void Step_WallAcrossTheSegment_ExpiresWithoutApplyingHits()
    {
        Harness h = new();
        _ = h.SpawnMonster(new Vector2(20f, 0f));
        Entity proj = h.Projectiles.Spawn(7UL, 3UL, fromPlayer: true, Vector2.Zero, new Vector2(100f, 0f), 1, 10f, false, 30U);
        h.Move(proj, Vector2.Zero, new Vector2(40f, 0f));
        h.Physics.WallHit = true;

        h.Projectiles.Step();

        h.Hits.MonsterHits.Should().BeEmpty();
        h.Projectiles.Count.Should().Be(0);
        h.Simulation.Entities.IsAlive(proj).Should().BeFalse();
    }

    [Theory]
    [InlineData(true, 0, 2U)]
    [InlineData(false, 1, 29U)]
    public void Step_MonsterProjectile_AsksHandlerForPlayerHitAndConsumesOnlyOnHit(bool playerHit, int expectedCount, uint expectedRemaining)
    {
        Harness h = new();
        h.Hits.PlayerHitResult = playerHit;
        Entity proj = h.Projectiles.Spawn(9UL, 0UL, fromPlayer: false, Vector2.Zero, new Vector2(100f, 0f), 1, 6f, false, 30U);
        h.Move(proj, Vector2.Zero, new Vector2(40f, 0f));

        h.Projectiles.Step();

        h.Hits.PlayerSegments.Should().Equal((Vector2.Zero, new Vector2(40f, 0f)));
        h.Projectiles.Count.Should().Be(expectedCount);
        if (expectedCount == 1)
        {
            h.Projectiles.TryGet(h.FirstNetworkId, out ProjectileState state).Should().BeTrue();
            state.RemainingTicks.Should().Be(expectedRemaining);
        }
    }

    [Fact]
    public void Step_NoHit_ExpiresWhenLifetimeRunsOut()
    {
        Harness h = new();
        Entity proj = h.Projectiles.Spawn(7UL, 3UL, fromPlayer: true, Vector2.Zero, new Vector2(100f, 0f), 1, 10f, false, 3U);

        h.Projectiles.Step();
        h.Projectiles.Step();
        h.Projectiles.Count.Should().Be(1, "3 ticks of lifetime survive two steps");

        h.Projectiles.Step();
        h.Projectiles.Count.Should().Be(0);
        h.Simulation.Entities.IsAlive(proj).Should().BeFalse();
    }

    [Fact]
    public void Forget_DespawnedProjectile_StopsTrackingIt()
    {
        Harness h = new();
        _ = h.Projectiles.Spawn(7UL, 3UL, fromPlayer: true, Vector2.Zero, Vector2.UnitX, 1, 10f, false, 30U);

        h.Projectiles.Forget(h.FirstNetworkId);

        h.Projectiles.TryGet(h.FirstNetworkId, out _).Should().BeFalse();
        h.Projectiles.Count.Should().Be(0);
    }

    [Fact]
    public void DistancePointToSegmentSq_PointBeyondSegmentEnd_ClampsToEndpoint()
    {
        float d = ProjectileService.DistancePointToSegmentSq(new Vector2(13f, 4f), Vector2.Zero, new Vector2(10f, 0f), out float t);

        t.Should().Be(1f);
        d.Should().BeApproximately(25f, 1e-4f);
    }

    private sealed class Harness
    {
        private ulong _nextNetworkId = 9000UL;

        public Harness() =>
            this.Projectiles = new ProjectileService(this.Simulation, this.Physics, MonsterLayer, () => ++_nextNetworkId, this.Hits);

        public WorldSimulation Simulation { get; } = new();

        public FakePhysics Physics { get; } = new();

        public RecordingHits Hits { get; } = new();

        public ProjectileService Projectiles { get; }

        public ulong FirstNetworkId => 9001UL;

        public Entity SpawnMonster(Vector2 position)
        {
            Entity mob = this.Simulation.SpawnEntity(position, 8f);
            this.Simulation.Entities.SetCollider(mob, new Collider2D(ColliderKind.Circle, 8f, default, false, MonsterLayer, 0U));
            this.Simulation.Entities.SetHealth(mob, new HealthComponent(50f, 50f));
            return mob;
        }

        public void Move(Entity entity, Vector2 from, Vector2 to)
        {
            this.Simulation.Entities.SetPreviousTransform(entity, new PreviousTransform2D(from));
            this.Simulation.Entities.SetTransform(entity, new Transform2D(to));
        }
    }

    private sealed class RecordingHits : IProjectileHitHandler
    {
        public List<Entity> MonsterHits { get; } = [];

        public Vector2 LastShotDirection { get; private set; }

        public List<(Vector2 Start, Vector2 End)> PlayerSegments { get; } = [];

        public bool PlayerHitResult { get; set; }

        public void OnMonsterHit(in ProjectileState projectile, Entity monster, Vector2 segmentStart, Vector2 shotDirection)
        {
            this.MonsterHits.Add(monster);
            this.LastShotDirection = shotDirection;
        }

        public bool TryHitPlayer(in ProjectileState projectile, Vector2 segmentStart, Vector2 segmentEnd)
        {
            this.PlayerSegments.Add((segmentStart, segmentEnd));
            return this.PlayerHitResult;
        }
    }

    private sealed class FakePhysics : IWorldPhysics2D
    {
        public bool WallHit { get; set; }

        public float GetMoveSpeedMultiplier(Vector2 worldPosition) => 1f;

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
            hit = default;
            return this.WallHit;
        }
    }
}
