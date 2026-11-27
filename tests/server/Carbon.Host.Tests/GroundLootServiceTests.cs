// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Numerics;
using Carbon.Application.World.Loot;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Simulation.World;
using FluentAssertions;
using Xunit;

namespace Carbon.Host.Tests;

public sealed class GroundLootServiceTests
{
    private const uint StackableItemId = 4U;

    [Fact]
    public void Spawn_SameStackableItemNearExistingStack_MergesIntoIt()
    {
        Harness h = new();

        h.Loot.Spawn(new Vector2(100f, 100f), new ItemSlot(StackableItemId, 3));
        h.Loot.Spawn(new Vector2(110f, 100f), new ItemSlot(StackableItemId, 2));

        h.Loot.Count.Should().Be(1, "the second drop lands within the merge radius of the first stack");
        h.Loot.TryGet(h.FirstNetworkId, out GroundLootEntry entry).Should().BeTrue();
        entry.Item.Quantity.Should().Be(5);
        h.ChangedStacks.Should().ContainSingle().Which.Should().Be(h.FirstNetworkId);
    }

    [Fact]
    public void Spawn_SameItemFarFromExistingStack_CreatesSecondStack()
    {
        Harness h = new();

        h.Loot.Spawn(new Vector2(100f, 100f), new ItemSlot(StackableItemId, 3));
        h.Loot.Spawn(new Vector2(200f, 100f), new ItemSlot(StackableItemId, 2));

        h.Loot.Count.Should().Be(2);
        h.ChangedStacks.Should().BeEmpty();
    }

    [Fact]
    public void CollectAutoPickupCandidates_DuringDropGracePeriod_ReturnsNothingThenTheStack()
    {
        Harness h = new();
        h.Loot.Spawn(new Vector2(100f, 100f), new ItemSlot(StackableItemId, 1));
        List<ulong> candidates = [];

        h.Loot.CollectAutoPickupCandidates(new Vector2(100f, 100f), System.Guid.Empty, candidates);
        candidates.Should().BeEmpty("a just-dropped stack is not auto-collected immediately");

        h.Tick += 12;
        h.Loot.CollectAutoPickupCandidates(new Vector2(100f, 100f), System.Guid.Empty, candidates);
        candidates.Should().ContainSingle().Which.Should().Be(h.FirstNetworkId);
    }

    [Fact]
    public void CollectAutoPickupCandidates_DropperStandingOnItsOwnDrop_SkipsItButOthersCollectIt()
    {
        Harness h = new();
        System.Guid dropper = System.Guid.NewGuid();
        h.Loot.SpawnDropped(new Vector2(100f, 100f), new ItemSlot(StackableItemId, 1), new LootClaim(dropper, 900UL, h.Tick, 180UL * 30UL));
        h.Tick += 60;
        List<ulong> candidates = [];

        h.Loot.CollectAutoPickupCandidates(new Vector2(100f, 100f), dropper, candidates);
        candidates.Should().BeEmpty("a player must not suck the item they just threw away straight back into the bag");

        h.Loot.CollectAutoPickupCandidates(new Vector2(100f, 100f), System.Guid.NewGuid(), candidates);
        candidates.Should().ContainSingle().Which.Should().Be(h.FirstNetworkId);
    }

    [Fact]
    public void CollectAutoPickupCandidates_DropperWalksAwayAndBack_CollectsItsDropAgain()
    {
        Harness h = new();
        System.Guid dropper = System.Guid.NewGuid();
        h.Loot.SpawnDropped(new Vector2(100f, 100f), new ItemSlot(StackableItemId, 1), new LootClaim(dropper, 900UL, h.Tick, 180UL * 30UL));
        h.Tick += 60;
        List<ulong> candidates = [];

        h.Loot.CollectAutoPickupCandidates(new Vector2(140f, 100f), dropper, candidates);
        h.Loot.CollectAutoPickupCandidates(new Vector2(100f, 100f), dropper, candidates);
        candidates.Should().BeEmpty("40 px is not far enough to re-arm auto-collect");

        h.Loot.CollectAutoPickupCandidates(new Vector2(200f, 100f), dropper, candidates);
        h.Loot.CollectAutoPickupCandidates(new Vector2(100f, 100f), dropper, candidates);
        candidates.Should().ContainSingle().Which.Should().Be(h.FirstNetworkId);
    }

    [Fact]
    public void SpawnDropped_WithProtection_NeverMergesAndIsReservedForTheDropper()
    {
        Harness h = new();
        System.Guid dropper = System.Guid.NewGuid();
        h.Loot.Spawn(new Vector2(100f, 100f), new ItemSlot(StackableItemId, 3));

        h.Loot.SpawnDropped(new Vector2(110f, 100f), new ItemSlot(StackableItemId, 2), new LootClaim(dropper, 900UL, h.Tick + (60UL * 30UL), 180UL * 30UL));

        h.Loot.Count.Should().Be(2, "a reserved drop must not slip its protection by merging into a public stack");
        h.Loot.TryGet(h.FirstNetworkId + 1UL, out GroundLootEntry dropped).Should().BeTrue();
        dropped.CanBePickedUpBy(System.Guid.NewGuid(), h.Tick + (60UL * 30UL) - 1UL).Should().BeFalse();
        dropped.CanBePickedUpBy(System.Guid.NewGuid(), h.Tick + (60UL * 30UL)).Should().BeTrue();
        dropped.DroppedBy.Should().Be(dropper);
    }

    [Fact]
    public void ForgetDeadStacks_AfterEntityDespawned_ForgetsTheStack()
    {
        Harness h = new();
        h.Loot.Spawn(new Vector2(100f, 100f), new ItemSlot(StackableItemId, 1));
        h.Simulation.TryGetEntityByNetworkId(h.FirstNetworkId, out Carbon.Simulation.ECS.Entity entity).Should().BeTrue();

        h.Simulation.DespawnEntity(entity);
        h.Loot.ForgetDeadStacks();

        h.Loot.Count.Should().Be(0);
    }

    private sealed class Harness
    {
        private ulong _nextNetworkId = 5000UL;

        public Harness()
        {
            ItemRules rules = ItemRules.LoadShippedDefaults();
            this.Loot = new GroundLootService(
                this.Simulation,
                () => rules,
                () => ++_nextNetworkId,
                () => this.Tick,
                (id, _) => this.ChangedStacks.Add(id));
        }

        public WorldSimulation Simulation { get; } = new();

        public GroundLootService Loot { get; }

        public ulong Tick { get; set; } = 100UL;

        public ulong FirstNetworkId => 5001UL;

        public List<ulong> ChangedStacks { get; } = [];
    }
}
