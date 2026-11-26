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

        h.Loot.CollectAutoPickupCandidates(new Vector2(100f, 100f), candidates);
        candidates.Should().BeEmpty("a just-dropped stack is not auto-collected immediately");

        h.Tick += 12;
        h.Loot.CollectAutoPickupCandidates(new Vector2(100f, 100f), candidates);
        candidates.Should().ContainSingle().Which.Should().Be(h.FirstNetworkId);
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
