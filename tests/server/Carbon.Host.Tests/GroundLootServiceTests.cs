// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Numerics;
using Carbon.Application.World.Loot;
using Carbon.Economy.Config;
using Carbon.Items.Catalog;
using Carbon.Items.Components;
using Carbon.Items.Containers;
using Carbon.Simulation.ECS;
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

    [Theory]
    [InlineData(10, 90, 900UL)] // audit MAGIC-1: config seconds at the world's own tick rate, not 120 s at 30 Hz
    [InlineData(30, 180, 5400UL)] // the shipped economy.json at 30 Hz: 180 s, which the old constant cut to 120 s
    [InlineData(20, 45, 900UL)]
    public void Spawn_UnclaimedStack_LivesTheConfiguredLifetimeAtTheWorldsTickRate(int tickRate, int lifetimeSeconds, ulong expectedTicks)
    {
        Harness h = new(tickRate, new GroundLootRules { KillLootLifetimeSeconds = lifetimeSeconds });

        h.Loot.Spawn(new Vector2(100f, 100f), new ItemSlot(StackableItemId, 3));

        h.Loot.TryGet(h.FirstNetworkId, out GroundLootEntry entry).Should().BeTrue();
        entry.LifetimeTicks.Should().Be(expectedTicks, "groundLoot.killLootLifetimeSeconds × tick rate");
        h.DespawnAtTick(h.FirstNetworkId).Should().Be(h.Tick + expectedTicks, "the entity expires when its entry does");
    }

    [Fact]
    public void Spawn_NoEconomyLoaded_FallsBackToTheDefaultLifetimeAtTheWorldsTickRate()
    {
        Harness h = new(tickRate: 10, rules: null);

        h.Loot.Spawn(new Vector2(100f, 100f), new ItemSlot(StackableItemId, 3));

        h.DespawnAtTick(h.FirstNetworkId).Should().Be(h.Tick + (180UL * 10UL), "the GroundLootRules default (180 s) at 10 Hz");
    }

    [Fact]
    public void Spawn_MergeIntoAStack_RefreshesItsExpiryToTheConfiguredLifetime()
    {
        Harness h = new(tickRate: 10, new GroundLootRules { KillLootLifetimeSeconds = 90 });
        h.Loot.Spawn(new Vector2(100f, 100f), new ItemSlot(StackableItemId, 3));
        h.Tick += 400UL;

        h.Loot.Spawn(new Vector2(110f, 100f), new ItemSlot(StackableItemId, 2));

        h.DespawnAtTick(h.FirstNetworkId).Should().Be(h.Tick + 900UL, "a top-up restarts the stack's lifetime");
    }

    [Fact]
    public void Spawn_RulesChangeAfterAReload_NewStacksUseTheNewLifetime()
    {
        GroundLootRules rules = new() { KillLootLifetimeSeconds = 90 };
        Harness h = new(tickRate: 10, rulesSource: () => rules);
        h.Loot.Spawn(new Vector2(100f, 100f), new ItemSlot(StackableItemId, 1));
        rules = new GroundLootRules { KillLootLifetimeSeconds = 30 };

        h.Loot.Spawn(new Vector2(400f, 100f), new ItemSlot(StackableItemId, 1));

        h.DespawnAtTick(h.FirstNetworkId + 1UL).Should().Be(h.Tick + 300UL, "the lifetime is read when the stack spawns");
    }

    private sealed class Harness
    {
        private ulong _nextNetworkId = 5000UL;

        public Harness()
            : this(tickRate: 30, rulesSource: null)
        {
        }

        public Harness(int tickRate, GroundLootRules? rules)
            : this(tickRate, () => rules)
        {
        }

        public Harness(int tickRate, System.Func<GroundLootRules?>? rulesSource)
        {
            ItemRules rules = ItemRules.LoadShippedDefaults();
            this.Simulation = new WorldSimulation(tickRate);
            this.Loot = new GroundLootService(
                this.Simulation,
                () => rules,
                () => ++_nextNetworkId,
                () => this.Tick,
                (id, _) => this.ChangedStacks.Add(id),
                groundLootRules: rulesSource);
        }

        public WorldSimulation Simulation { get; }

        public GroundLootService Loot { get; }

        public ulong Tick { get; set; } = 100UL;

        public ulong FirstNetworkId => 5001UL;

        public List<ulong> ChangedStacks { get; } = [];

        public ulong DespawnAtTick(ulong networkId)
        {
            this.Simulation.TryGetEntityByNetworkId(networkId, out Entity entity).Should().BeTrue();
            return this.Simulation.Entities.GetComponent<LootDropComponent>(entity).DespawnAtTick;
        }
    }
}
