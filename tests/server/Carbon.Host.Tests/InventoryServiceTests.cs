// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.Application.World.Inventory;
using Carbon.Application.World.Loot;
using Carbon.Application.World.Players;
using Carbon.Application.World.Replication;
using Carbon.Contracts.Common;
using Carbon.Economy.Enhancement;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Crafting;
using Carbon.Items.Definitions;
using Carbon.Maps.Building;
using Carbon.Simulation.World;
using FluentAssertions;
using Xunit;

namespace Carbon.Host.Tests;

public sealed class InventoryServiceTests
{
    private const float TileSize = 32f;
    private const uint StackableItemId = 4U;

    [Fact]
    public void Move_TwoBagSlots_SwapsThemAndReportsBoth()
    {
        Harness h = new();
        h.Player.Inventory.SetSlot(0, new ItemSlot(StackableItemId, 3));
        List<(ushort SlotIndex, ItemSlot Slot)> modified = [];

        h.Inventory.Move(h.Player, 0, 5, modified).Should().Be(OpStatus.Ok);

        h.Player.Inventory.GetSlot(5).Should().Be(new ItemSlot(StackableItemId, 3));
        h.Player.Inventory.GetSlot(0).IsEmpty.Should().BeTrue();
        modified.Should().HaveCount(2);
    }

    [Fact]
    public void Move_SlotOutsideTheInventory_IsInvalid()
    {
        Harness h = new();

        h.Inventory.Move(h.Player, 0, ushort.MaxValue, []).Should().Be(OpStatus.Invalid);
    }

    [Fact]
    public void Drop_PartOfAStack_KeepsTheRestAndSpawnsTheDroppedPartAsLoot()
    {
        Harness h = new();
        h.Player.Inventory.SetSlot(2, new ItemSlot(StackableItemId, 5));
        List<(ushort SlotIndex, ItemSlot Slot)> modified = [];

        h.Inventory.Drop(h.Player, 2, 2, new Vector2(100f, 100f), modified).Should().Be(OpStatus.Ok);

        h.Player.Inventory.GetSlot(2).Quantity.Should().Be(3);
        modified.Should().Equal((2, new ItemSlot(StackableItemId, 3)));
        h.GroundLoot.Count.Should().Be(1);
        h.GroundLoot.TryGet(h.LastLootNetworkId, out GroundLootEntry dropped).Should().BeTrue();
        dropped.Item.Quantity.Should().Be(2);
        dropped.DroppedBy.Should().Be(h.Player.Session.CharacterId, "the dropper's own auto-collect must ignore it");
    }

    [Fact]
    public void Drop_WithShippedEconomy_ReservesTheStackForTheDropperLikeKillLoot()
    {
        Harness h = new() { Economy = Carbon.Economy.GameEconomy.LoadFromDirectory(Carbon.Economy.GameEconomy.FindConfigDirectory(AppContext.BaseDirectory)!) };
        h.Player.Inventory.SetSlot(2, new ItemSlot(StackableItemId, 5));

        h.Inventory.Drop(h.Player, 2, 0, new Vector2(100f, 100f), []).Should().Be(OpStatus.Ok);

        h.GroundLoot.TryGet(h.LastLootNetworkId, out GroundLootEntry dropped).Should().BeTrue();
        dropped.CanBePickedUpBy(Guid.NewGuid(), 100UL + (59UL * 30UL)).Should().BeFalse("another player waits out the drop protection");
        dropped.CanBePickedUpBy(Guid.NewGuid(), 100UL + (60UL * 30UL)).Should().BeTrue("after groundLoot.ownerProtectionSeconds (60 s) anyone may take it");
        dropped.CanBePickedUpBy(h.Player.Session.CharacterId, 100UL).Should().BeTrue("the dropper can always take it back by hand");
    }

    [Fact]
    public void Drop_NoEconomyLoaded_LivesTheDefaultGroundLootLifetimeAtTheTickRate()
    {
        Harness h = new();
        h.Player.Inventory.SetSlot(2, new ItemSlot(StackableItemId, 5));

        h.Inventory.Drop(h.Player, 2, 0, new Vector2(100f, 100f), []).Should().Be(OpStatus.Ok);

        h.GroundLoot.TryGet(h.LastLootNetworkId, out GroundLootEntry dropped).Should().BeTrue();
        dropped.LifetimeTicks.Should().Be(180UL * 30UL, "the GroundLootRules default (180 s) at 30 Hz, not a hard-coded 120 s (audit MAGIC-1)");
    }

    [Fact]
    public void Drop_EmptySlot_IsNotFound()
    {
        Harness h = new();

        h.Inventory.Drop(h.Player, 1, 0, Vector2.Zero, []).Should().Be(OpStatus.NotFound);
        h.GroundLoot.Count.Should().Be(0);
    }

    [Fact]
    public void TryPickup_LootInReach_MovesItIntoTheBagAndReplicatesTheSlot()
    {
        Harness h = new();
        h.GroundLoot.Spawn(new Vector2(10f, 0f), new ItemSlot(StackableItemId, 4));

        h.Inventory.TryPickup(h.Player, h.LastLootNetworkId, 36f * 36f).Should().BeTrue();

        h.Player.Inventory.CountItem(StackableItemId).Should().Be(4);
        h.Outbound.Pending.SlotChanges.Should().NotBeEmpty();
        h.DirtyMarks.Should().Be(1);
    }

    [Fact]
    public void TryPickup_LootOutOfReach_TakesNothing()
    {
        Harness h = new();
        h.GroundLoot.Spawn(new Vector2(500f, 0f), new ItemSlot(StackableItemId, 4));

        h.Inventory.TryPickup(h.Player, h.LastLootNetworkId, 36f * 36f).Should().BeFalse();

        h.Player.Inventory.CountItem(StackableItemId).Should().Be(0);
        h.DirtyMarks.Should().Be(0);
    }

    [Fact]
    public void Upgrade_AwayFromAnyForge_IsOutOfRange()
    {
        Harness h = new();

        h.Inventory.Upgrade(h.Player, 0, 0, Vector2.Zero, 1U, new Dictionary<ulong, PlayerState>().Keys, [], out ForgeAttempt attempt, out uint experience)
            .Should().Be(OpStatus.OutOfRange);
        attempt.Outcome.Should().Be(EnhancementOutcome.None);
        experience.Should().Be(0U);
    }

    [Fact]
    public void IsNearForgeStation_IntactAnvilWithinFiveTiles_IsTrue()
    {
        Harness h = new();
        h.Buildings.ForcePlaceStructure(4, 0, StructureType.ForgeAnvil);

        h.Inventory.IsNearForgeStation(new Vector2(0.5f * TileSize, 0.5f * TileSize)).Should().BeTrue();
        h.Inventory.IsNearForgeStation(new Vector2(20f * TileSize, 0f)).Should().BeFalse();
    }

    [Fact]
    public void Craft_StationRecipeAwayFromItsStation_IsOutOfRange()
    {
        Harness h = new();
        CraftingRecipe? stationRecipe = null;
        foreach (CraftingRecipe recipe in h.Rules.Recipes.All)
        {
            if (recipe.Station == CraftingStation.Workbench)
            {
                stationRecipe = recipe;
                break;
            }
        }

        stationRecipe.Should().NotBeNull("the shipped recipes include workbench recipes");
        h.Inventory.Craft(h.Player, stationRecipe!, 1, Vector2.Zero, [], out int crafted).Should().Be(OpStatus.OutOfRange);
        crafted.Should().Be(0);
    }

    [Fact]
    public void ResolveHarvestTier_ToolInTheSlot_ReturnsItsTier()
    {
        Harness h = new();
        ItemArchetype? bestTool = null;
        foreach (ItemArchetype item in h.Rules.Items.All)
        {
            if (item.Category == ItemCategory.Tool && (bestTool is null || item.HarvestTier > bestTool.HarvestTier))
            {
                bestTool = item;
            }
        }

        h.Player.Inventory.SetSlot(1, new ItemSlot(bestTool!.Id, 1));

        h.Inventory.ResolveHarvestTier(h.Player.Inventory, 1).Should().Be(bestTool.HarvestTier);
        h.Inventory.ResolveHarvestTier(h.Player.Inventory, 0).Should().Be(0);
    }

    internal sealed class Harness
    {
        private ulong _nextNetworkId = 7000UL;

        public Harness()
        {
            this.GroundLoot = new GroundLootService(this.Simulation, () => this.Rules, () => this.LastLootNetworkId = ++_nextNetworkId, () => 100UL, (_, _) => { });
            this.Inventory = new InventoryService(
                this.Simulation,
                this.Buildings,
                TileSize,
                TestGameplayEntropy.Create(42UL),
                () => this.Rules,
                () => this.Economy,
                () => 100UL,
                (_, _, _) => false,
                this.GroundLoot,
                this.Outbound,
                _ => this.DirtyMarks++);

            ConnectedPlayerSession session = new(9UL, Guid.NewGuid(), Guid.NewGuid(), "Tester", 900UL, 0, 0);
            CharacterRecord character = new(
                session.CharacterId, session.AccountId, "Tester", 1, 0, 0f, 0f, 100f, 100f, 0, [], DateTimeOffset.UnixEpoch);
            this.Player = new PlayerState(
                session,
                this.Simulation.SpawnNetworkEntity(session.PlayerNetworkId, Vector2.Zero, 7f),
                character,
                new InventoryContainer(PlayerInventoryLayout.TotalCapacity));
        }

        public ItemRules Rules { get; } = ItemRules.LoadShippedDefaults();

        public Carbon.Economy.GameEconomy? Economy { get; set; }

        public WorldSimulation Simulation { get; } = new();

        public BuildingGrid Buildings { get; } = new();

        public OutboundTickBuffer Outbound { get; } = new();

        public GroundLootService GroundLoot { get; }

        public InventoryService Inventory { get; }

        public PlayerState Player { get; }

        public ulong LastLootNetworkId { get; private set; }

        public int DirtyMarks { get; private set; }
    }
}
