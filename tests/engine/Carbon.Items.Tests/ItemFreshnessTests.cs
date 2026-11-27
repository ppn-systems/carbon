// Copyright (c) 2026 PPN Systems. All rights reserved.

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
    public void InventoryBinaryCodec_RetiredFormatVersion_IsRefused()
    {
        // A well-formed v3 payload relabelled as v2: the length fits, only the version byte is old.
        InventoryContainer source = new(8);
        source.SetSlot(1, new ItemSlot(RawMeat, 7, FreshnessSeconds: 600f));
        byte[] payload = InventoryBinaryCodec.Serialize(source);
        payload[0] = 2;

        InventoryContainer target = new(8);
        target.SetSlot(0, new ItemSlot(RawMeat, 1));
        InventoryBinaryCodec.TryDeserializeInto(payload, target).Should().BeFalse("only the current format v3 is read; there are no saves to keep");
        target.GetSlot(0).Should().Be(new ItemSlot(RawMeat, 1), "a refused payload leaves the target untouched");
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
