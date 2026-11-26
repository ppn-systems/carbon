// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Buffers.Binary;
using Carbon.Items.Containers;
using Carbon.Items.Serialization;
using FluentAssertions;
using Xunit;

namespace Carbon.Items.Tests;

public sealed class ItemFreshnessTests
{
    private const uint RawMeat = 121;

    [Fact]
    public void InventoryBinaryCodec_V3_RoundTripsFreshness()
    {
        InventoryContainer source = new(8);
        source.SetSlot(2, new ItemSlot(RawMeat, 5, FreshnessSeconds: 734.5f));
        source.SetSlot(3, new ItemSlot(4, 20));

        byte[] blob = InventoryBinaryCodec.Serialize(source);
        blob[0].Should().Be(3, "the codec writes format v3");

        InventoryContainer copy = new(8);
        InventoryBinaryCodec.TryDeserializeInto(blob, copy).Should().BeTrue();
        copy.GetSlot(2).FreshnessSeconds.Should().Be(734.5f);
        copy.GetSlot(3).FreshnessSeconds.Should().Be(0f, "non-food stays not perishable");
    }

    [Fact]
    public void InventoryBinaryCodec_LegacyV2Payload_LoadsAsNotPerishable()
    {
        // v2 header (version, capacity, count) + one 23-byte entry: slot, item, quantity, instance, durability, upgrade.
        byte[] v2 = new byte[5 + 23];
        v2[0] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(v2.AsSpan(1, 2), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(v2.AsSpan(3, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(v2.AsSpan(5, 2), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(v2.AsSpan(7, 4), RawMeat);
        BinaryPrimitives.WriteInt32LittleEndian(v2.AsSpan(11, 4), 7);

        InventoryContainer target = new(8);
        InventoryBinaryCodec.TryDeserializeInto(v2, target).Should().BeTrue("old saves must keep loading");
        ItemSlot slot = target.GetSlot(1);
        slot.ItemId.Should().Be(RawMeat);
        slot.Quantity.Should().Be(7);
        slot.FreshnessSeconds.Should().Be(0f);
        slot.IsPerishable.Should().BeFalse();
    }

    [Fact]
    public void TryAdd_MergingStacks_KeepsTheStalestFreshness()
    {
        InventoryContainer bag = new(4);
        bag.SetSlot(0, new ItemSlot(RawMeat, 3, FreshnessSeconds: 900f));

        bag.TryAdd(new ItemSlot(RawMeat, 2, FreshnessSeconds: 300f)).Should().Be(0);
        bag.GetSlot(0).Quantity.Should().Be(5);
        bag.GetSlot(0).FreshnessSeconds.Should().Be(300f, "a merged stack spoils with its oldest units");

        bag.TryAdd(new ItemSlot(RawMeat, 1)).Should().Be(0, "fresh, unstamped loot merges in");
        bag.GetSlot(0).FreshnessSeconds.Should().Be(300f, "an unstamped stack does not reset the countdown");
    }

    [Fact]
    public void TryMoveOrSwap_MergingStacks_KeepsTheStalestFreshness()
    {
        InventoryContainer bag = new(4);
        bag.SetSlot(0, new ItemSlot(RawMeat, 2, FreshnessSeconds: 120f));
        bag.SetSlot(1, new ItemSlot(RawMeat, 2, FreshnessSeconds: 600f));

        bag.TryMoveOrSwap(0, 1).Should().BeTrue();

        bag.GetSlot(1).Quantity.Should().Be(4);
        bag.GetSlot(1).FreshnessSeconds.Should().Be(120f);
        bag.GetSlot(0).IsEmpty.Should().BeTrue();
    }

    [Theory]
    [InlineData(0f, 0f, 0f)]
    [InlineData(0f, 50f, 50f)]
    [InlineData(80f, 0f, 80f)]
    [InlineData(80f, 50f, 50f)]
    public void MergeFreshness_TreatsZeroAsUnstamped(float a, float b, float expected) =>
        ItemSlot.MergeFreshness(a, b).Should().Be(expected);
}
