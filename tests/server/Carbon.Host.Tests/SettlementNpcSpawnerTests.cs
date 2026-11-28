// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Carbon.AI.Components;
using Carbon.AI.Threat;
using Carbon.Application.World.Actors;
using Carbon.Contracts.Entities;
using Carbon.Items.Catalog;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Landmarks;
using Carbon.Maps.Settlements;
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
    public void PopulateAround_TravelOutAndBack_KeepsOnlyResidentChunksAndSpawnsNothingTwice()
    {
        Harness h = new();
        const float chunkWorld = Chunk2D.Size * 32f;
        Vector2 spawn = h.Chunks.SpawnWorldPosition;
        Vector2 start = new((MathF.Floor(spawn.X / chunkWorld) + 0.5f) * chunkWorld, (MathF.Floor(spawn.Y / chunkWorld) + 0.5f) * chunkWorld);
        int actorsOut = 0;
        for (int leg = 0; leg < 2; leg++)
        {
            for (int i = 0; i <= 30; i++)
            {
                int c = leg == 0 ? i : 30 - i;
                Vector2 at = start + new Vector2(c * chunkWorld, 0f);
                h.Chunks.UpdateAroundPosition(at, chunkRadius: 2, evictionRadius: 8);
                h.Spawner.PopulateAround(at, radius: 2);
            }

            actorsOut = leg == 0 ? h.Actors.Count : actorsOut;
        }

        h.Actors.Count.Should().Be(actorsOut, "the way back passes only sites already populated");

        // ALLOC-7: an evicted chunk leaves the set; the 35 columns of 5 passed are not all kept.
        h.Spawner.PopulatedChunkCount.Should().BeLessThanOrEqualTo(h.Chunks.LoadedChunkCount);
    }

    [Fact]
    public void PopulateAround_ChunksNotLoaded_RecordsNoneUntilTheyAre()
    {
        Harness h = new();
        Vector2 far = new(100.5f * Chunk2D.Size * 32f, 0.5f * Chunk2D.Size * 32f);

        h.Spawner.PopulateAround(far, radius: 2);
        int before = h.Spawner.PopulatedChunkCount;
        h.Chunks.UpdateAroundPosition(far, chunkRadius: 2, evictionRadius: 8);
        h.Spawner.PopulateAround(far, radius: 2);

        before.Should().Be(0, "a chunk not loaded is not recorded, so no eviction would ever remove it");
        h.Spawner.PopulatedChunkCount.Should().Be(25);
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

    [Fact]
    public void PopulateAround_Village_BuildsItOnceWithOneGuardAndTheBlacksmithAtTheForge()
    {
        Harness h = new(withVillages: true);
        PointOfInterest village = FindVillage(h.Chunks.Generator);
        Vector2 centre = new((village.TileX + 0.5f) * 32f, (village.TileY + 0.5f) * 32f);
        h.Chunks.UpdateAroundPosition(centre, chunkRadius: 2, evictionRadius: 8);

        h.Spawner.PopulateAround(centre, radius: 2);
        int structures = h.Chunks.Buildings.StructureCount;
        int actors = h.Actors.Count;
        h.Spawner.PopulateAround(centre, radius: 2);
        h.Spawner.EnsureVillageBuilt(Chunk2D.ToChunkCoord(village.TileX), Chunk2D.ToChunkCoord(village.TileY));

        structures.Should().BeGreaterThan(100, "the village is built from real structures");
        h.Chunks.Buildings.StructureCount.Should().Be(structures, "a village is built once");
        h.Actors.Count.Should().Be(actors);
        h.Dirty.Should().NotBeEmpty("built chunks are persisted");
        h.Actors.Values.Count(a => a.ArchetypeId == WorldActorCatalog.VillageGuard && Near(a.HomePos, centre, 3f)).Should().Be(1, "one guard per village, not one per chunk");
        NpcActorMeta smith = h.Actors.Values.Single(a => a.ArchetypeId == WorldActorCatalog.MasterBlacksmith && Near(a.HomePos, centre, 17f));
        h.Spawner.TryFindNpcTile(village, VillageBuildingRole.Smithy, out int sx, out int sy).Should().BeTrue();
        smith.HomePos.Should().Be(new Vector2((sx + 0.5f) * 32f, (sy + 0.5f) * 32f), "the blacksmith works at the smithy");
        bool anvilNear = false;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                anvilNear |= h.Chunks.Buildings.TryGetStructure(sx + dx, sy + dy, out StructureTile t) && t.Type == StructureType.ForgeAnvil;
            }
        }

        anvilNear.Should().BeTrue("the smithy's NPC spot is next to its forge anvil");
    }

    [Fact]
    public void VillageStructures_PlayerDeconstructAndDamage_AreRefused()
    {
        Harness h = new(withVillages: true);
        PointOfInterest village = FindVillage(h.Chunks.Generator);
        h.Spawner.EnsureVillageBuilt(Chunk2D.ToChunkCoord(village.TileX), Chunk2D.ToChunkCoord(village.TileY));
        BuildingService service = BuildingService.ForWorld(h.Chunks, StructureCatalog.LoadEmbedded(), new BuildPermissionTable());
        KeyValuePair<(int X, int Y), StructureTile> wall = h.Chunks.Buildings.PlacedStructures.First(kv => kv.Value.Category == StructureCategory.Wall);
        BuildActor player = new(StructureOwners.ToPlayerOwnerId(77UL), wall.Key.X, wall.Key.Y + 1, ToolTier: 9);

        service.ApplyDeconstructionWork(player, wall.Key.X, wall.Key.Y, 1e9f, new NullSink()).Code.Should().Be(BuildResultCode.ProtectedSettlement);
        service.ApplyPlayerDamage(player.ActorId, wall.Key.X, wall.Key.Y, 1e9f, null).Code.Should().Be(BuildResultCode.ProtectedSettlement);
        h.Chunks.Buildings.TryGetStructure(wall.Key.X, wall.Key.Y, out StructureTile after).Should().BeTrue();
        after.CurrentDurability.Should().Be(wall.Value.CurrentDurability);
    }

    private static bool Near(Vector2 a, Vector2 b, float tiles) => Vector2.Distance(a, b) <= tiles * 32f;

    private static PointOfInterest FindVillage(ProceduralWorldGenerator generator)
    {
        for (int ring = 0; ring < 6; ring++)
        {
            for (int sy = -ring; sy <= ring; sy++)
            {
                for (int sx = -ring; sx <= ring; sx++)
                {
                    PointOfInterest poi = generator.Landmarks.GetSite(sx, sy);
                    if (poi.Type == PointOfInterestType.RiverVillage)
                    {
                        bool hasSmithy = false;
                        foreach (VillagePlot plot in generator.Landmarks.GetLayout(sx, sy).Plots)
                        {
                            hasSmithy |= plot.Role == VillageBuildingRole.Smithy;
                        }

                        if (hasSmithy)
                        {
                            return poi;
                        }
                    }
                }
            }
        }

        throw new System.InvalidOperationException("No river village with a smithy near the origin.");
    }

    private static NpcActorMeta Meta(EntityKind kind, float attackRange) => new(
        kind, 3, "Test", 2, new Vector2(64f, 64f), 120f, 50f, 9f, 5f, 0U, 0L, 1U, 1, 10U, attackRange);

    private sealed class Harness
    {
        private ulong _nextNetworkId = 4000UL;

        public Harness(bool withVillages = false) =>
            this.Spawner = new SettlementNpcSpawner(
                this.Simulation,
                this.Chunks,
                this.Actors,
                () => ++_nextNetworkId,
                32f,
                30,
                7f,
                PlayerLayer,
                MonsterLayer,
                NpcLayer,
                withVillages ? new VillageMaterializer(this.Chunks, VillageBuildingCatalog.Default, StructureCatalog.LoadEmbedded()) : null,
                this.Dirty);

        public HashSet<long> Dirty { get; } = [];

        public WorldSimulation Simulation { get; } = new();

        public ChunkWorld2D Chunks { get; } = new(new ProceduralWorldGenerator(1337UL), 32f, 0.0008f);

        public Dictionary<ulong, NpcActorMeta> Actors { get; } = new();

        public SettlementNpcSpawner Spawner { get; }
    }

    private sealed class NullSink : IMaterialSink
    {
        public void Add(string itemId, int quantity)
        {
        }
    }
}
