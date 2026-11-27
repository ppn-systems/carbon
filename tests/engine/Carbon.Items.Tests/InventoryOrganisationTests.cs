// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Buffers.Binary;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Serialization;
using FluentAssertions;
using Xunit;

namespace Carbon.Items.Tests;

/// <summary>ADR-020: stack split, backpack sort, accessory slots, one-unit-per-slot adds, and codec hardening.</summary>
public sealed class InventoryOrganisationTests
{
    private static readonly ItemRules s_rules = ItemRules.LoadShippedDefaults();
    private static readonly uint s_slimeRing = s_rules.ItemCatalog.ResolveId("slime_ring");
    private static readonly uint s_bloodAmulet = s_rules.ItemCatalog.ResolveId("blood_amulet");

    [Fact]
    public void TrySplitStack_IntoEmptyBagSlot_MovesTheQuantityAndKeepsTheRest()
    {
        InventoryContainer inv = new(PlayerInventoryLayout.TotalCapacity);
        inv.SetSlot(9, new ItemSlot(StarterGameCatalog.WoodLog, 40, FreshnessSeconds: 0f));

        PlayerInventoryLayout.TrySplitStack(s_rules, inv, 9, 20, 15).Should().BeTrue();

        inv.GetSlot(9).Quantity.Should().Be(25);
        inv.GetSlot(20).Should().Be(new ItemSlot(StarterGameCatalog.WoodLog, 15));
        inv.CountItem(StarterGameCatalog.WoodLog).Should().Be(40, "a split never creates or destroys units");
    }

    [Fact]
    public void TrySplitStack_OntoMatchingStackWithRoom_MergesAndKeepsTheStalestFreshness()
    {
        InventoryContainer inv = new(PlayerInventoryLayout.TotalCapacity);
        inv.SetSlot(9, new ItemSlot(StarterGameCatalog.BerryHerb, 10, FreshnessSeconds: 50f));
        inv.SetSlot(10, new ItemSlot(StarterGameCatalog.BerryHerb, 5, FreshnessSeconds: 300f));

        PlayerInventoryLayout.TrySplitStack(s_rules, inv, 9, 10, 4).Should().BeTrue();

        inv.GetSlot(9).Quantity.Should().Be(6);
        inv.GetSlot(10).Quantity.Should().Be(9);
        inv.GetSlot(10).FreshnessSeconds.Should().Be(50f, "merged food keeps the stalest countdown (ADR-016)");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(10)]
    [InlineData(11)]
    public void TrySplitStack_QuantityNotLeavingAtLeastOne_IsRejected(int quantity)
    {
        InventoryContainer inv = new(PlayerInventoryLayout.TotalCapacity);
        inv.SetSlot(9, new ItemSlot(StarterGameCatalog.WoodLog, 10));

        PlayerInventoryLayout.TrySplitStack(s_rules, inv, 9, 12, quantity).Should().BeFalse();

        inv.GetSlot(9).Quantity.Should().Be(10);
        inv.GetSlot(12).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void TrySplitStack_InvalidTargets_AreRejectedWithoutChanges()
    {
        InventoryContainer inv = new(PlayerInventoryLayout.TotalCapacity);
        int maxLogs = s_rules.GetMaxStackSize(StarterGameCatalog.WoodLog);
        inv.SetSlot(9, new ItemSlot(StarterGameCatalog.WoodLog, 10));
        inv.SetSlot(10, new ItemSlot(StarterGameCatalog.StoneChunk, 3));
        inv.SetSlot(11, new ItemSlot(StarterGameCatalog.WoodLog, maxLogs - 2));
        inv.SetSlot(12, new ItemSlot(StarterGameCatalog.WoodLog, 4, UpgradeLevel: 1));
        ItemSlot[] before = inv.Slots.ToArray();

        PlayerInventoryLayout.TrySplitStack(s_rules, inv, 9, 10, 2).Should().BeFalse("the target holds another item");
        PlayerInventoryLayout.TrySplitStack(s_rules, inv, 9, 11, 3).Should().BeFalse("the target stack has room for 2, not 3");
        PlayerInventoryLayout.TrySplitStack(s_rules, inv, 12, 13, 2).Should().BeFalse("enhanced items never stack");
        PlayerInventoryLayout.TrySplitStack(s_rules, inv, 9, PlayerInventoryLayout.HeadSlot, 2).Should().BeFalse("splits stay in the bag");
        PlayerInventoryLayout.TrySplitStack(s_rules, inv, 9, 9, 2).Should().BeFalse();
        PlayerInventoryLayout.TrySplitStack(s_rules, inv, 14, 15, 1).Should().BeFalse("the source is empty");

        inv.Slots.ToArray().Should().Equal(before);
    }

    [Fact]
    public void SortBackpack_MixedBag_MergesStacksOrdersGearFirstAndConservesEveryItem()
    {
        InventoryContainer inv = new(PlayerInventoryLayout.TotalCapacity);
        inv.SetSlot(0, new ItemSlot(StarterGameCatalog.WoodLog, 3));
        inv.SetSlot(PlayerInventoryLayout.ChestSlot, new ItemSlot(StarterGameCatalog.LeatherTunic, 1, 0, 150f));
        inv.SetSlot(9, new ItemSlot(StarterGameCatalog.StoneChunk, 7));
        inv.SetSlot(11, new ItemSlot(StarterGameCatalog.WoodLog, 20));
        inv.SetSlot(14, new ItemSlot(StarterGameCatalog.IronSword, 1, 0, 200f));
        inv.SetSlot(17, new ItemSlot(StarterGameCatalog.WoodLog, 30));
        inv.SetSlot(20, new ItemSlot(StarterGameCatalog.DemonCleaver, 1, 0, 300f));
        inv.SetSlot(33, new ItemSlot(StarterGameCatalog.LeatherCap, 1, 0, 120f));

        PlayerInventoryLayout.TotalCapacity.Should().Be(42);
        bool changed = InventorySorting.SortBackpack(s_rules, inv);

        changed.Should().BeTrue();
        inv.GetSlot(9).ItemId.Should().Be(StarterGameCatalog.DemonCleaver, "weapons sort first and the epic cleaver outranks the sword");
        inv.GetSlot(10).ItemId.Should().Be(StarterGameCatalog.IronSword);
        inv.GetSlot(11).ItemId.Should().Be(StarterGameCatalog.LeatherCap, "armor follows weapons");
        inv.GetSlot(12).Should().Be(new ItemSlot(StarterGameCatalog.WoodLog, 50), "the two partial log stacks merged into one");
        inv.GetSlot(13).ItemId.Should().Be(StarterGameCatalog.StoneChunk);
        inv.GetSlot(14).IsEmpty.Should().BeTrue("empty slots go last");
        inv.GetSlot(0).Should().Be(new ItemSlot(StarterGameCatalog.WoodLog, 3), "the hotbar is never sorted");
        inv.GetSlot(PlayerInventoryLayout.ChestSlot).ItemId.Should().Be(StarterGameCatalog.LeatherTunic, "equipment is never sorted");
        inv.CountItem(StarterGameCatalog.WoodLog).Should().Be(53);
        inv.OccupiedSlotCount.Should().Be(7);
    }

    [Fact]
    public void SortBackpack_SameItemsInAnyOrder_GivesTheSameLayoutAndIsIdempotent()
    {
        ItemSlot[] items =
        [
            new(StarterGameCatalog.IronSword, 1, 0, 120f),
            new(StarterGameCatalog.IronSword, 1, 0, 200f, UpgradeLevel: 3),
            new(StarterGameCatalog.BerryHerb, 4, FreshnessSeconds: 90f),
            new(StarterGameCatalog.BerryHerb, 2, FreshnessSeconds: 30f),
            new(StarterGameCatalog.ManaCrystal, 5),
            new(StarterGameCatalog.WoodWallKit, 6),
        ];

        InventoryContainer forward = new(PlayerInventoryLayout.TotalCapacity);
        InventoryContainer reversed = new(PlayerInventoryLayout.TotalCapacity);
        for (int i = 0; i < items.Length; i++)
        {
            forward.SetSlot(9 + (i * 3), items[i]);
            reversed.SetSlot(35 - (i * 2), items[i]);
        }

        _ = InventorySorting.SortBackpack(s_rules, forward);
        _ = InventorySorting.SortBackpack(s_rules, reversed);

        reversed.Slots.ToArray().Should().Equal(forward.Slots.ToArray(), "the sort is a total order, so the start order does not matter");
        forward.GetSlot(9).UpgradeLevel.Should().Be(3, "the enhanced copy comes first");
        InventorySorting.SortBackpack(s_rules, forward).Should().BeFalse("a sorted backpack does not change again");
    }

    [Fact]
    public void SortBackpack_AfterWarmUp_AllocatesNothing()
    {
        InventoryContainer inv = new(PlayerInventoryLayout.TotalCapacity);
        _ = InventorySorting.SortBackpack(s_rules, inv);
        inv.SetSlot(30, new ItemSlot(StarterGameCatalog.StoneChunk, 2));
        inv.SetSlot(31, new ItemSlot(StarterGameCatalog.IronSword, 1, 0, 200f));

        long before = GC.GetAllocatedBytesForCurrentThread();
        _ = InventorySorting.SortBackpack(s_rules, inv);

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().Be(0, "the sort works on stack buffers (AGENTS.md rule 4)");
    }

    [Fact]
    public void AccessorySlots_AcceptRingsAndAmuletsOnlyAndQuickEquipFillsTheFreeOne()
    {
        InventoryContainer inv = new(PlayerInventoryLayout.TotalCapacity);
        inv.SetSlot(9, new ItemSlot(s_slimeRing, 1));
        inv.SetSlot(10, new ItemSlot(s_bloodAmulet, 1));
        inv.SetSlot(11, new ItemSlot(StarterGameCatalog.LeatherCap, 1, 0, 120f));

        PlayerInventoryLayout.CanAcceptSlotItem(s_rules, PlayerInventoryLayout.Accessory2Slot, new ItemSlot(s_slimeRing, 1)).Should().BeTrue();
        PlayerInventoryLayout.TryMoveOrEquipSlot(s_rules, inv, 11, PlayerInventoryLayout.Accessory1Slot).Should().BeFalse("a helmet is not an accessory");
        PlayerInventoryLayout.TryMoveOrEquipSlot(s_rules, inv, 9, PlayerInventoryLayout.HeadSlot).Should().BeFalse("a ring is not a helmet");

        PlayerInventoryLayout.TryQuickMove(s_rules, inv, 9, out int first).Should().BeTrue();
        PlayerInventoryLayout.TryQuickMove(s_rules, inv, 10, out int second).Should().BeTrue();

        first.Should().Be(PlayerInventoryLayout.Accessory1Slot);
        second.Should().Be(PlayerInventoryLayout.Accessory2Slot, "the second accessory takes the free slot");
        inv.GetSlot(PlayerInventoryLayout.Accessory2Slot).ItemId.Should().Be(s_bloodAmulet);

        PlayerInventoryLayout.TryQuickMove(s_rules, inv, PlayerInventoryLayout.Accessory1Slot, out int back).Should().BeTrue();
        back.Should().BeInRange(0, PlayerInventoryLayout.BagLimit - 1, "unequipping returns the ring to the bag");
    }

    [Fact]
    public void TryAddBagItem_NonStackableEntryWithQuantityTwo_TakesOneSlotPerUnit()
    {
        InventoryContainer inv = new(PlayerInventoryLayout.TotalCapacity);

        int remainder = PlayerInventoryLayout.TryAddBagItem(inv, new ItemSlot(StarterGameCatalog.IronSword, 2, 0, 200f), 1);

        remainder.Should().Be(0);
        inv.GetSlot(0).Quantity.Should().Be(1);
        inv.GetSlot(1).Should().Be(new ItemSlot(StarterGameCatalog.IronSword, 1, 0, 200f));
        inv.CountItem(StarterGameCatalog.IronSword).Should().Be(2);
    }

    [Fact]
    public void InventoryContainerTryAdd_NonStackableWithoutRoom_ReportsTheUnitsLeftOver()
    {
        InventoryContainer inv = new(2);

        inv.TryAdd(new ItemSlot(StarterGameCatalog.IronSword, 3, 0, 200f), maxStackSize: 1).Should().Be(1);

        inv.OccupiedSlotCount.Should().Be(2);
    }

    [Fact]
    public void InventoryBinaryCodec_EveryFieldOnEverySlot_RoundTripsExactly()
    {
        InventoryContainer source = new(PlayerInventoryLayout.TotalCapacity);
        for (int i = 0; i < PlayerInventoryLayout.BagLimit; i++)
        {
            source.SetSlot(i, new ItemSlot(StarterGameCatalog.BerryHerb, i + 1, FreshnessSeconds: 10f + i));
        }

        source.SetSlot(PlayerInventoryLayout.HeadSlot, new ItemSlot(StarterGameCatalog.IronHelmet, 1, 77UL, 123.5f, UpgradeLevel: 9));
        source.SetSlot(PlayerInventoryLayout.FeetSlot, new ItemSlot(StarterGameCatalog.LeatherBoots, 1, 0UL, 0f));
        source.SetSlot(PlayerInventoryLayout.Accessory1Slot, new ItemSlot(s_slimeRing, 1));
        source.SetSlot(PlayerInventoryLayout.Accessory2Slot, new ItemSlot(s_bloodAmulet, 1, 5UL));

        InventoryContainer restored = new(PlayerInventoryLayout.TotalCapacity);
        InventoryBinaryCodec.TryDeserializeInto(InventoryBinaryCodec.Serialize(source), restored).Should().BeTrue();

        restored.Slots.ToArray().Should().Equal(source.Slots.ToArray());
        restored.OccupiedSlotCount.Should().Be(source.OccupiedSlotCount);
    }

    [Fact]
    public void InventoryBinaryCodec_Forty_SlotBlob_LoadsIntoTheLargerInventory()
    {
        InventoryContainer legacy = new(40);
        legacy.SetSlot(PlayerInventoryLayout.FeetSlot, new ItemSlot(StarterGameCatalog.IronBoots, 1, 0, 200f));

        InventoryContainer restored = new(PlayerInventoryLayout.TotalCapacity);
        InventoryBinaryCodec.TryDeserializeInto(InventoryBinaryCodec.Serialize(legacy), restored).Should().BeTrue();

        restored.GetSlot(PlayerInventoryLayout.FeetSlot).ItemId.Should().Be(StarterGameCatalog.IronBoots);
        restored.GetSlot(PlayerInventoryLayout.Accessory1Slot).IsEmpty.Should().BeTrue();
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(-5f)]
    public void InventoryBinaryCodec_CorruptDurability_LoadsAsBroken(float durability)
    {
        InventoryContainer source = new(PlayerInventoryLayout.TotalCapacity);
        source.SetSlot(2, new ItemSlot(StarterGameCatalog.IronSword, 1, 0, durability));

        InventoryContainer restored = new(PlayerInventoryLayout.TotalCapacity);
        InventoryBinaryCodec.TryDeserializeInto(InventoryBinaryCodec.Serialize(source), restored).Should().BeTrue();

        restored.GetSlot(2).Durability.Should().Be(0f, "a NaN durability would otherwise never wear out");
    }

    [Fact]
    public void InventoryBinaryCodec_RepeatedSlotIndex_KeepsTheFirstEntry()
    {
        InventoryContainer source = new(PlayerInventoryLayout.TotalCapacity);
        source.SetSlot(4, new ItemSlot(StarterGameCatalog.WoodLog, 12));
        source.SetSlot(5, new ItemSlot(StarterGameCatalog.StoneChunk, 3));
        byte[] payload = InventoryBinaryCodec.Serialize(source);

        // Rewrite the second entry's slot index (offset 5 + 27) to 4, as a corrupt blob would.
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(5 + 27, 2), 4);
        InventoryContainer restored = new(PlayerInventoryLayout.TotalCapacity);
        InventoryBinaryCodec.TryDeserializeInto(payload, restored).Should().BeTrue();

        restored.GetSlot(4).Should().Be(new ItemSlot(StarterGameCatalog.WoodLog, 12));
        restored.OccupiedSlotCount.Should().Be(1);
    }
}
