// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Application.World.Inventory;
using Carbon.Application.World.Loot;
using Carbon.Contracts.Common;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using FluentAssertions;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>ADR-020: server validation of move, split, sort, destroy and equip, item-bound commands, and conservation.</summary>
public sealed class InventorySlotCommandTests
{
    private const uint Logs = StarterGameCatalog.WoodLog;
    private const uint Stone = StarterGameCatalog.StoneChunk;

    [Fact]
    public void Execute_SplitIntoEmptySlot_MovesUnitsAndReportsBothSlots()
    {
        InventoryServiceTests.Harness h = new();
        h.Player.Inventory.SetSlot(9, new ItemSlot(Logs, 30));
        List<(ushort SlotIndex, ItemSlot Slot)> modified = [];

        h.Inventory.Execute(h.Player, new InventorySlotCommand(InventorySlotCommandKind.Split, 9, 15, 12, (ushort)Logs), Vector2.Zero, modified)
            .Should().Be(OpStatus.Ok);

        modified.Should().Equal((9, new ItemSlot(Logs, 18)), (15, new ItemSlot(Logs, 12)));
    }

    [Fact]
    public void Execute_SplitWholeStackOrOntoOtherItem_IsInvalidAndChangesNothing()
    {
        InventoryServiceTests.Harness h = new();
        h.Player.Inventory.SetSlot(9, new ItemSlot(Logs, 30));
        h.Player.Inventory.SetSlot(10, new ItemSlot(Stone, 2));

        h.Inventory.Execute(h.Player, new InventorySlotCommand(InventorySlotCommandKind.Split, 9, 11, 30), Vector2.Zero, [])
            .Should().Be(OpStatus.Invalid);
        h.Inventory.Execute(h.Player, new InventorySlotCommand(InventorySlotCommandKind.Split, 9, 10, 5), Vector2.Zero, [])
            .Should().Be(OpStatus.Invalid);
        h.Inventory.Execute(h.Player, new InventorySlotCommand(InventorySlotCommandKind.Split, 12, 13, 1), Vector2.Zero, [])
            .Should().Be(OpStatus.NotFound);

        h.Player.Inventory.GetSlot(9).Quantity.Should().Be(30);
        h.Player.Inventory.GetSlot(11).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Execute_SortScatteredBackpack_ReportsOnlyChangedSlotsAndASecondSortReportsNone()
    {
        InventoryServiceTests.Harness h = new();
        h.Player.Inventory.SetSlot(0, new ItemSlot(Stone, 1));
        h.Player.Inventory.SetSlot(20, new ItemSlot(Logs, 4));
        h.Player.Inventory.SetSlot(30, new ItemSlot(StarterGameCatalog.IronSword, 1, 0, 200f));
        List<(ushort SlotIndex, ItemSlot Slot)> modified = [];

        h.Inventory.Execute(h.Player, new InventorySlotCommand(InventorySlotCommandKind.Sort, 0), Vector2.Zero, modified).Should().Be(OpStatus.Ok);

        modified.Should().BeEquivalentTo(new (ushort, ItemSlot)[]
        {
            (9, new ItemSlot(StarterGameCatalog.IronSword, 1, 0, 200f)),
            (10, new ItemSlot(Logs, 4)),
            (20, ItemSlot.Empty),
            (30, ItemSlot.Empty),
        });
        h.Player.Inventory.GetSlot(0).ItemId.Should().Be(Stone, "the hotbar is not sorted");

        h.Inventory.Execute(h.Player, new InventorySlotCommand(InventorySlotCommandKind.Sort, 0), Vector2.Zero, modified).Should().Be(OpStatus.Ok);
        modified.Should().BeEmpty();
    }

    [Fact]
    public void Execute_DestroyPartOfAStack_RemovesItWithoutSpawningLoot()
    {
        InventoryServiceTests.Harness h = new();
        h.Player.Inventory.SetSlot(9, new ItemSlot(Logs, 10));
        List<(ushort SlotIndex, ItemSlot Slot)> modified = [];

        h.Inventory.Execute(h.Player, new InventorySlotCommand(InventorySlotCommandKind.Destroy, 9, Quantity: 4, ExpectedItemId: (ushort)Logs), Vector2.Zero, modified)
            .Should().Be(OpStatus.Ok);
        h.Inventory.Execute(h.Player, new InventorySlotCommand(InventorySlotCommandKind.Destroy, 9, Quantity: -1), Vector2.Zero, modified)
            .Should().Be(OpStatus.Ok);

        h.Player.Inventory.GetSlot(9).IsEmpty.Should().BeTrue();
        modified.Should().Equal((9, ItemSlot.Empty));
        h.GroundLoot.Count.Should().Be(0, "destroyed items never reach the ground");
    }

    [Fact]
    public void Execute_DropAfterTheSlotChanged_IsConflictAndDropsNothing()
    {
        InventoryServiceTests.Harness h = new();
        h.Player.Inventory.SetSlot(9, new ItemSlot(StarterGameCatalog.LeatherCap, 1, 0, 120f));
        h.Player.Inventory.SetSlot(10, new ItemSlot(StarterGameCatalog.CrystalCrown, 1, 0, 240f));

        // The client saw the cap in slot 9 and asked to drop it, but a move swapped in the crown first.
        h.Inventory.Execute(h.Player, new InventorySlotCommand(InventorySlotCommandKind.Move, 9, 10, ExpectedItemId: (ushort)StarterGameCatalog.LeatherCap), Vector2.Zero, [])
            .Should().Be(OpStatus.Ok);
        h.Inventory.Execute(h.Player, new InventorySlotCommand(InventorySlotCommandKind.Drop, 9, Quantity: -1, ExpectedItemId: (ushort)StarterGameCatalog.LeatherCap), Vector2.Zero, [])
            .Should().Be(OpStatus.Conflict);

        h.Player.Inventory.GetSlot(9).ItemId.Should().Be(StarterGameCatalog.CrystalCrown, "the rare crown stays in the bag");
        h.GroundLoot.Count.Should().Be(0);
    }

    [Fact]
    public void Execute_ReplayedMove_IsConflictInsteadOfSwappingBack()
    {
        InventoryServiceTests.Harness h = new();
        h.Player.Inventory.SetSlot(9, new ItemSlot(Logs, 5));
        h.Player.Inventory.SetSlot(10, new ItemSlot(Stone, 2));
        InventorySlotCommand move = new(InventorySlotCommandKind.Move, 9, 10, ExpectedItemId: (ushort)Logs);

        h.Inventory.Execute(h.Player, in move, Vector2.Zero, []).Should().Be(OpStatus.Ok);
        h.Inventory.Execute(h.Player, in move, Vector2.Zero, []).Should().Be(OpStatus.Conflict, "slot 9 now holds stone, not the logs the client saw");

        h.Player.Inventory.GetSlot(10).ItemId.Should().Be(Logs);
    }

    [Theory]
    [InlineData(StarterGameCatalog.LeatherCap, PlayerInventoryLayout.HeadSlot, OpStatus.Ok)]
    [InlineData(StarterGameCatalog.LeatherCap, PlayerInventoryLayout.ChestSlot, OpStatus.Invalid)]
    [InlineData(StarterGameCatalog.LeatherCap, PlayerInventoryLayout.Accessory1Slot, OpStatus.Invalid)]
    [InlineData(StarterGameCatalog.IronSword, PlayerInventoryLayout.FeetSlot, OpStatus.Invalid)]
    [InlineData(StarterGameCatalog.WoodLog, PlayerInventoryLayout.Accessory2Slot, OpStatus.Invalid)]
    public void Execute_MoveIntoEquipmentSlot_OnlyAcceptsMatchingGear(uint itemId, int equipSlot, OpStatus expected)
    {
        InventoryServiceTests.Harness h = new();
        h.Player.Inventory.SetSlot(9, new ItemSlot(itemId, 1, 0, 100f));

        h.Inventory.Execute(h.Player, new InventorySlotCommand(InventorySlotCommandKind.Move, 9, (ushort)equipSlot), Vector2.Zero, [])
            .Should().Be(expected);
    }

    [Fact]
    public void Execute_RingIntoEitherAccessorySlot_IsAcceptedAndSwapOutRequiresAFittingItem()
    {
        InventoryServiceTests.Harness h = new();
        ushort ring = (ushort)h.Rules.ItemCatalog.ResolveId("slime_ring");
        h.Player.Inventory.SetSlot(9, new ItemSlot(ring, 1));
        h.Player.Inventory.SetSlot(10, new ItemSlot(Logs, 3));

        h.Inventory.Execute(h.Player, new InventorySlotCommand(InventorySlotCommandKind.Move, 9, PlayerInventoryLayout.Accessory2Slot), Vector2.Zero, [])
            .Should().Be(OpStatus.Ok);
        h.Inventory.Execute(h.Player, new InventorySlotCommand(InventorySlotCommandKind.Move, 10, PlayerInventoryLayout.Accessory2Slot), Vector2.Zero, [])
            .Should().Be(OpStatus.Invalid, "swapping logs into the ring's slot would put logs in an accessory slot");
        h.Inventory.Execute(h.Player, new InventorySlotCommand(InventorySlotCommandKind.Move, PlayerInventoryLayout.Accessory2Slot, PlayerInventoryLayout.Accessory1Slot), Vector2.Zero, [])
            .Should().Be(OpStatus.Ok);

        h.Player.Inventory.GetSlot(PlayerInventoryLayout.Accessory1Slot).ItemId.Should().Be(ring);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(1337)]
    public void Execute_ShuffledAndReplayedCommands_NeverDuplicateItems(int seed)
    {
        InventoryServiceTests.Harness h = new();
        InventoryContainer inv = h.Player.Inventory;
        inv.SetSlot(0, new ItemSlot(Logs, 40));
        inv.SetSlot(1, new ItemSlot(Stone, 15));
        inv.SetSlot(9, new ItemSlot(Logs, 25));
        inv.SetSlot(10, new ItemSlot(Stone, 30));
        inv.SetSlot(11, new ItemSlot(Logs, 60));
        inv.SetSlot(12, new ItemSlot(StarterGameCatalog.BerryHerb, 8, FreshnessSeconds: 120f));
        inv.SetSlot(13, new ItemSlot(StarterGameCatalog.BerryHerb, 6, FreshnessSeconds: 40f));
        inv.SetSlot(16, new ItemSlot(StarterGameCatalog.IronHelmet, 1, 0, 220f));
        inv.SetSlot(14, new ItemSlot(StarterGameCatalog.LeatherCap, 1, 0, 120f));
        inv.SetSlot(15, new ItemSlot(StarterGameCatalog.IronSword, 1, 0, 200f, UpgradeLevel: 2));
        inv.SetSlot(PlayerInventoryLayout.ChestSlot, new ItemSlot(StarterGameCatalog.LeatherTunic, 1, 0, 150f));
        Dictionary<uint, int> initial = Totals(h);

        // Commands are generated against the starting bag (what a client would have seen), then run shuffled, twice.
        Random random = new(seed);
        List<InventorySlotCommand> commands = [];
        for (int i = 0; i < 400; i++)
        {
            ushort a = RandomSlot(random);
            ushort b = RandomSlot(random);
            InventorySlotCommandKind kind = random.Next(20) switch
            {
                < 8 => InventorySlotCommandKind.Move,
                < 14 => InventorySlotCommandKind.Split,
                < 16 => InventorySlotCommandKind.Drop,
                < 18 => InventorySlotCommandKind.Destroy,
                _ => InventorySlotCommandKind.Sort,
            };
            int quantity = kind == InventorySlotCommandKind.Split ? random.Next(1, 20) : random.Next(-1, 30);
            ushort expected = random.Next(4) == 0 || a >= inv.Capacity ? (ushort)0 : (ushort)inv.GetSlot(a).ItemId;
            commands.Add(new InventorySlotCommand(kind, a, b, quantity, expected));
        }

        Dictionary<uint, int> destroyed = [];
        Dictionary<OpStatus, int> statuses = [];
        HashSet<InventorySlotCommandKind> applied = [];
        List<(ushort SlotIndex, ItemSlot Slot)> modified = [];
        for (int pass = 0; pass < 2; pass++)
        {
            Shuffle(random, commands);
            foreach (InventorySlotCommand command in commands)
            {
                ItemSlot before = command.SlotA < inv.Capacity ? inv.GetSlot(command.SlotA) : ItemSlot.Empty;
                OpStatus status = h.Inventory.Execute(h.Player, in command, new Vector2(200f * pass, 0f), modified);
                statuses[status] = statuses.GetValueOrDefault(status) + 1;
                if (status == OpStatus.Ok)
                {
                    _ = applied.Add(command.Kind);
                }

                if (status == OpStatus.Ok && command.Kind == InventorySlotCommandKind.Destroy)
                {
                    int removed = before.Quantity - inv.GetSlot(command.SlotA).Quantity;
                    destroyed[before.ItemId] = destroyed.GetValueOrDefault(before.ItemId) + removed;
                }

                AssertSlotsAreLegal(h);
                Dictionary<uint, int> now = Totals(h);
                foreach ((uint itemId, int count) in initial)
                {
                    now.GetValueOrDefault(itemId).Should().Be(
                        count - destroyed.GetValueOrDefault(itemId),
                        $"item {itemId} must be conserved (bag + ground - destroyed) after {command}");
                }

                now.Keys.Should().BeSubsetOf(initial.Keys, "no command may create an item that was not there");
            }
        }

        applied.Should().HaveCount(Enum.GetValues<InventorySlotCommandKind>().Length, "every command kind must actually run for the check to mean anything");
        statuses.GetValueOrDefault(OpStatus.Conflict).Should().BePositive("some replayed commands must hit a slot that changed");
    }

    private static ushort RandomSlot(Random random) => random.Next(10) switch
    {
        0 => (ushort)random.Next(PlayerInventoryLayout.HeadSlot, PlayerInventoryLayout.TotalCapacity),
        1 => (ushort)random.Next(0, PlayerInventoryLayout.HotbarCount),
        2 => (ushort)(PlayerInventoryLayout.TotalCapacity + random.Next(3)),
        _ => (ushort)random.Next(PlayerInventoryLayout.BackpackStart, 24),
    };

    private static void Shuffle(Random random, List<InventorySlotCommand> commands)
    {
        for (int i = commands.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (commands[i], commands[j]) = (commands[j], commands[i]);
        }
    }

    private static Dictionary<uint, int> Totals(InventoryServiceTests.Harness h)
    {
        Dictionary<uint, int> totals = [];
        foreach (ItemSlot slot in h.Player.Inventory.Slots)
        {
            if (!slot.IsEmpty)
            {
                totals[slot.ItemId] = totals.GetValueOrDefault(slot.ItemId) + slot.Quantity;
            }
        }

        for (ulong id = 7001UL; id <= h.LastLootNetworkId; id++)
        {
            if (h.GroundLoot.TryGet(id, out GroundLootEntry loot))
            {
                totals[loot.Item.ItemId] = totals.GetValueOrDefault(loot.Item.ItemId) + loot.Item.Quantity;
            }
        }

        return totals;
    }

    private static void AssertSlotsAreLegal(InventoryServiceTests.Harness h)
    {
        ReadOnlySpan<ItemSlot> slots = h.Player.Inventory.Slots;
        for (int i = 0; i < slots.Length; i++)
        {
            ItemSlot slot = slots[i];
            if (slot.IsEmpty)
            {
                continue;
            }

            slot.Quantity.Should().BeLessThanOrEqualTo(Math.Max(40, h.Rules.GetMaxStackSize(slot.ItemId)), $"slot {i} is over its stack size");
            if (!slot.IsStackable || h.Rules.GetMaxStackSize(slot.ItemId) == 1)
            {
                slot.Quantity.Should().Be(1, $"slot {i} holds a non-stackable item");
            }

            PlayerInventoryLayout.CanAcceptSlotItem(h.Rules, i, in slot).Should().BeTrue($"slot {i} must accept {slot.ItemId}");
            if (i >= PlayerInventoryLayout.BagLimit)
            {
                h.Rules.Items.TryGet(slot.ItemId, out ItemArchetype? def).Should().BeTrue();
                def!.EquipSlot.Should().NotBe(EquipmentSlotKind.None);
            }
        }
    }
}
