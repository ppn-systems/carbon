// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Carbon.AI.Components;
using Carbon.AI.Threat;
using Carbon.Application.World.Actors;
using Carbon.Contracts.Entities;
using Carbon.Items.Catalog;
using Carbon.Maps.Generators;
using Carbon.Maps.Streaming;
using Carbon.Simulation.ECS;
using Carbon.Simulation.World;
using FluentAssertions;
using Xunit;

namespace Carbon.Host.Tests;

public sealed class SettlementNpcSpawnerTests
{
    private const uint PlayerLayer = 1U;
    private const uint MonsterLayer = 2U;
    private const uint NpcLayer = 4U;

    [Fact]
    public void PopulateAround_TwiceAtTheSpawn_PlacesTheSanctuaryBlacksmithOnceAndEachChunkOnce()
    {
        Harness h = new();
        Vector2 spawn = h.Chunks.SpawnWorldPosition;
        h.Chunks.UpdateAroundPosition(spawn, chunkRadius: 2, evictionRadius: 8);

        h.Spawner.PopulateAround(spawn, radius: 1);
        int actorsAfterFirst = h.Actors.Count;
        h.Spawner.PopulateAround(spawn, radius: 1);

        h.Actors.Values.Count(a => a.ArchetypeId == WorldActorCatalog.MasterBlacksmith).Should().BeGreaterThanOrEqualTo(1);
        h.Actors.Count.Should().Be(actorsAfterFirst, "chunks are populated once");
        h.Spawner.PopulatedChunkCount.Should().Be(9);
    }

    [Fact]
    public void SpawnActor_Monster_GetsTheMonsterLayerAThreatTableAndAnAggressiveBrain()
    {
        Harness h = new();
        NpcActorMeta meta = Meta(EntityKind.Monster, attackRange: 40f);

        h.Spawner.SpawnActor(in meta);

        (ulong netId, NpcActorMeta registered) = h.Actors.Single();
        registered.Should().Be(meta);
        h.Simulation.TryGetEntityByNetworkId(netId, out Entity entity).Should().BeTrue();
        h.Simulation.Entities.GetCollider(entity).Layer.Should().Be(MonsterLayer);
        h.Simulation.Entities.GetAttachmentStore<ThreatTable>().TryGet(entity, out _).Should().BeTrue();
        AiBrainComponent brain = h.Simulation.Entities.GetComponent<AiBrainComponent>(entity);
        brain.AggroRadius.Should().Be(192f);
        brain.AttackRange.Should().Be(40f);
        brain.TargetLayerMask.Should().Be(PlayerLayer);
        h.Simulation.Entities.GetHealth(entity).Max.Should().Be(meta.MaxHealth);
    }

    [Fact]
    public void SpawnActor_Npc_IsPassiveOnTheNpcLayer()
    {
        Harness h = new();
        NpcActorMeta meta = Meta(EntityKind.Npc, attackRange: 44f);

        h.Spawner.SpawnActor(in meta);

        h.Simulation.TryGetEntityByNetworkId(h.Actors.Keys.Single(), out Entity entity).Should().BeTrue();
        h.Simulation.Entities.GetCollider(entity).Layer.Should().Be(NpcLayer);
        h.Simulation.Entities.GetAttachmentStore<ThreatTable>().TryGet(entity, out _).Should().BeFalse();
        AiBrainComponent brain = h.Simulation.Entities.GetComponent<AiBrainComponent>(entity);
        brain.AggroRadius.Should().Be(0f);
        brain.TargetLayerMask.Should().Be(0U);
    }

    private static NpcActorMeta Meta(EntityKind kind, float attackRange) => new(
        kind, 3, "Test", 2, new Vector2(64f, 64f), 120f, 50f, 9f, 5f, 0U, 0L, 1U, 1, 10U, attackRange);

    private sealed class Harness
    {
        private ulong _nextNetworkId = 4000UL;

        public Harness() =>
            this.Spawner = new SettlementNpcSpawner(this.Simulation, this.Chunks, this.Actors, () => ++_nextNetworkId, 32f, 30, 7f, PlayerLayer, MonsterLayer, NpcLayer);

        public WorldSimulation Simulation { get; } = new();

        public ChunkWorld2D Chunks { get; } = new(new ProceduralWorldGenerator(1337UL), 32f, 0.0008f);

        public Dictionary<ulong, NpcActorMeta> Actors { get; } = new();

        public SettlementNpcSpawner Spawner { get; }
    }
}
