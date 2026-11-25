// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.AI.Components;
using Carbon.AI.Navigation;
using Carbon.AI.Systems;
using Carbon.AI.Threat;
using Carbon.Maps.Biomes;
using Carbon.Maps.Building;
using Carbon.Maps.Generators;
using Carbon.Maps.Navigation;
using Carbon.Maps.Streaming;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.World;
using FluentAssertions;
using Xunit;

namespace Carbon.AI.Tests;

public sealed class AiModuleTests
{
    [Fact]
    public void ThreatTable_SelectHighestThreatTarget_PrunesDeadAndReturnsTopTarget()
    {
        EntityManager entities = new(16);
        Entity low = entities.CreateEntity();
        Entity high = entities.CreateEntity();
        entities.SetHealth(high, new HealthComponent(0f, 100f)); // Already dead

        ThreatTable table = new();
        table.AddThreat(low, 25f);
        table.AddThreat(high, 100f);

        table.SelectHighestThreatTarget(entities).Should().Be(low);
        table.Count.Should().Be(1);
    }

    [Fact]
    public void PathWaypointBuffer_WithGridPathfinder_SteersAroundWall()
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(42UL), tileSize: 32f);
        (int sx, int sy) = world.SpawnTile;
        _ = world.Buildings.TryPlaceStructure(sx + 1, sy, StructureType.StoneWall, BiomeType.LushPlains, false, out _);

        GridPathfinder pathfinder = new(512);
        PathWaypointBuffer waypoints = new(64);
        Vector2 startPos = new((sx + 0.5f) * 32f, (sy + 0.5f) * 32f);
        Vector2 goalPos = new((sx + 2.5f) * 32f, (sy + 0.5f) * 32f);

        PathSearchStatus status = waypoints.TryComputePath(pathfinder, world.NavGrid, startPos, goalPos, 32f);
        status.Should().Be(PathSearchStatus.Found);
        waypoints.HasPath.Should().BeTrue();

        waypoints.TryGetSteeringDirection(startPos, 32f, arrivalRadius: 4f, out Vector2 dir).Should().BeTrue();
        dir.Length().Should().BeApproximately(1f, 0.001f);
    }

    [Fact]
    public void AiBehaviorSystem_AggroAttackAndLeashReturnHome_RestoresHealthAndResetsIdle()
    {
        WorldSimulation world = new(tickRate: 20);
        world.RegisterSystem(new AiBehaviorSystem());

        Entity player = world.SpawnEntity(new Vector2(60f, 0f), radius: 5f);
        world.Entities.SetCollider(player, new Collider2D(ColliderKind.Circle, Radius: 5f, Layer: 2));
        world.Entities.SetHealth(player, new HealthComponent(100f, 100f));

        Entity mob = world.SpawnEntity(new Vector2(0f, 0f), radius: 5f);
        world.Entities.SetCollider(mob, new Collider2D(ColliderKind.Circle, Radius: 5f, Layer: 4));
        world.Entities.SetVelocity(mob, new Velocity2D(0f, 0f));
        world.Entities.SetHealth(mob, new HealthComponent(40f, 100f));
        world.Entities.SetComponent(mob, new AiBrainComponent(
            State: AiState.Idle,
            HomePosition: Vector2.Zero,
            AggroRadius: 80f,
            AttackRange: 18f,
            LeashRadius: 90f,
            MoveSpeed: 100f,
            TargetLayerMask: 2,
            AttackCooldownTicks: 5));

        // 1. Idle -> Chase
        _ = world.Update(0.05);
        world.Entities.GetComponent<AiBrainComponent>(mob).State.Should().Be(AiState.Chase);
        world.Entities.GetComponent<AiBrainComponent>(mob).TargetEntity.Should().Be(player);

        // 2. Chase -> Attack range -> Attack event
        int attackReadyEvents = 0;
        for (int i = 0; i < 15; i++)
        {
            _ = world.Update(0.05);
            attackReadyEvents += world.ReadEvents<AiAttackReadyEvent>().Length;
        }

        world.Entities.GetComponent<AiBrainComponent>(mob).State.Should().Be(AiState.Attack);
        attackReadyEvents.Should().BeGreaterThanOrEqualTo(1);

        // 3. Kite mob past LeashRadius -> ReturnHome -> arrives home and heals back to 100
        world.EnqueueCommand(SimulationCommand.Teleport(mob, new Vector2(120f, 40f)));
        _ = world.Update(0.05);
        world.Entities.GetComponent<AiBrainComponent>(mob).State.Should().Be(AiState.ReturnHome);

        // Move player outside home aggro radius so mob remains Idle after resetting at home
        world.EnqueueCommand(SimulationCommand.Teleport(player, new Vector2(250f, 0f)));
        for (int i = 0; i < 35; i++)
        {
            _ = world.Update(0.05);
        }

        world.Entities.GetComponent<AiBrainComponent>(mob).State.Should().Be(AiState.Idle);
        world.Entities.GetHealth(mob).Current.Should().BeApproximately(100f, 0.001f);
    }
}
