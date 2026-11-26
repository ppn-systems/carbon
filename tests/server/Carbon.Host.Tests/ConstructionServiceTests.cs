// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Abstractions.World;
using Carbon.Application.World.Construction;
using Carbon.Application.World.Replication;
using Carbon.Contracts.Common;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Maps.Building;
using Carbon.Maps.Generators;
using Carbon.Maps.Streaming;
using Carbon.Simulation.ECS;
using Carbon.Simulation.World;
using FluentAssertions;
using Xunit;

namespace Carbon.Host.Tests;

public sealed class ConstructionServiceTests
{
    private const float TileSize = 32f;

    [Fact]
    public void TryCreateActor_PlayerCarryingTools_UsesItsTileAndBestToolTier()
    {
        Harness h = new();
        Entity player = h.Simulation.SpawnEntity(new Vector2((3 * TileSize) + 5f, (-2 * TileSize) + 1f), 7f);
        ItemArchetype bestTool = h.BestTool();
        ItemSlot[] inventory = [ItemSlot.Empty, new ItemSlot(bestTool.Id, 1), ItemSlot.Empty];

        h.Construction.TryCreateActor(player, Guid.NewGuid(), inventory, out BuildActor actor).Should().BeTrue();

        (actor.TileX, actor.TileY).Should().Be((3, -2));
        actor.ToolTier.Should().Be(bestTool.HarvestTier);
        actor.ActorId.Should().NotBe(0UL);
    }

    [Fact]
    public void TryCreateActor_SameCharacter_GetsTheSameOwnerId()
    {
        Harness h = new();
        Entity player = h.Simulation.SpawnEntity(Vector2.Zero, 7f);
        Guid character = Guid.NewGuid();

        _ = h.Construction.TryCreateActor(player, character, [], out BuildActor first);
        _ = h.Construction.TryCreateActor(player, character, [], out BuildActor second);

        second.ActorId.Should().Be(first.ActorId, "ownership must survive reconnects of the same character");
    }

    [Fact]
    public void TryCreateActor_DespawnedEntity_Fails()
    {
        Harness h = new();
        Entity player = h.Simulation.SpawnEntity(Vector2.Zero, 7f);
        h.Simulation.DespawnEntity(player);

        h.Construction.TryCreateActor(player, Guid.NewGuid(), [], out _).Should().BeFalse();
    }

    [Fact]
    public void DamageStructure_DestroyingAWall_ReplicatesTheEmptyTileClearsBarsAndMarksTheChunkDirty()
    {
        Harness h = new();
        h.ChunkWorld.Buildings.ForcePlaceStructure(4, 5, StructureType.WoodWall);

        h.Construction.DamageStructure(4, 5, 1_000_000f);

        h.ChunkWorld.Buildings.TryGetStructure(4, 5, out _).Should().BeFalse();
        h.Outbound.Pending.StructureChanges.Should().ContainSingle(c => c.TileX == 4 && c.TileY == 5)
            .Which.StructureType.Should().Be((byte)StructureType.None);
        h.Outbound.Pending.ConstructionChanges.Should().ContainSingle(c => c.TileX == 4 && c.TileY == 5 && c.IsDeconstruction);
        h.DirtyChunks.Should().NotBeEmpty();
    }

    [Fact]
    public void DamageStructure_EmptyTile_ChangesNothing()
    {
        Harness h = new();

        h.Construction.DamageStructure(4, 5, 10f);

        h.Outbound.Pending.StructureChanges.Should().BeEmpty();
        h.DirtyChunks.Should().BeEmpty();
        h.ChunkWorld.Buildings.StructureCount.Should().Be(0);
    }

    [Fact]
    public void StepJob_NoJob_DoesNothing()
    {
        Harness h = new();
        BuildJob job = new();

        h.Construction.StepJob(job, new BuildActor(1UL, 0, 0, 0), new NoMaterials(), 10f);

        job.Kind.Should().Be(BuildJobKind.None);
        h.Outbound.Pending.ConstructionChanges.Should().BeEmpty();
    }

    [Fact]
    public void ToOutcome_ReportsTheRevisionOnlyOnSuccess()
    {
        ConstructionService.ToOutcome(BuildMutationResult.Fail(BuildResultCode.PermissionDenied), 7U)
            .Should().Be(new WorldMutationOutcome(OpStatus.Forbidden, 0U));
    }

    [Fact]
    public void ChunkRevision_UnmodifiedChunk_IsZero()
    {
        Harness h = new();

        h.Construction.ChunkRevision(100, 100).Should().Be(0U);
    }

    private sealed class Harness
    {
        private readonly ItemRules _rules = ItemRules.LoadShippedDefaults();

        public Harness()
        {
            this.ChunkWorld = new ChunkWorld2D(new ProceduralWorldGenerator(42UL), TileSize, 0.0008f);
            this.Construction = new ConstructionService(
                this.ChunkWorld,
                StructureCatalog.LoadEmbedded(),
                (_, _) => false,
                this.Simulation,
                TileSize,
                () => _rules,
                this.Outbound,
                this.DirtyChunks,
                (_, _) => (0, 1),
                (pos, item) => this.Drops.Add((pos, item)));
        }

        public WorldSimulation Simulation { get; } = new();

        public ChunkWorld2D ChunkWorld { get; }

        public OutboundTickBuffer Outbound { get; } = new();

        public HashSet<long> DirtyChunks { get; } = [];

        public List<(Vector2 Position, ItemSlot Item)> Drops { get; } = [];

        public ConstructionService Construction { get; }

        public ItemArchetype BestTool()
        {
            ItemArchetype? best = null;
            foreach (ItemArchetype item in _rules.Items.All)
            {
                if (item.Category == ItemCategory.Tool && (best is null || item.HarvestTier > best.HarvestTier))
                {
                    best = item;
                }
            }

            return best ?? throw new InvalidOperationException("The shipped catalog has no tools.");
        }
    }

    private sealed class NoMaterials : IMaterialSource
    {
        public int CountOf(string itemId) => 0;

        public int Take(string itemId, int quantity) => 0;

        public void Add(string itemId, int quantity)
        {
        }
    }
}
