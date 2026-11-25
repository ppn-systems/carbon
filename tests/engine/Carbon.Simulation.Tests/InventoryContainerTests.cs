// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Simulation.Items;
using FluentAssertions;
using Xunit;

namespace Carbon.Simulation.Tests;

public sealed class InventoryContainerTests
{
    [Fact]
    public void TryAdd_StackableItems_MergesExistingStacksAndReturnsOverflowRemainder()
    {
        InventoryContainer bag = new(capacity: 2);

        bag.TryAdd(new ItemSlot(ItemId: 10, Quantity: 60), maxStackSize: 99).Should().Be(0);
        bag.TryAdd(new ItemSlot(ItemId: 10, Quantity: 60), maxStackSize: 99).Should().Be(0);

        bag.OccupiedSlotCount.Should().Be(2);
        bag.GetSlot(0).Quantity.Should().Be(99);
        bag.GetSlot(1).Quantity.Should().Be(21);
        bag.CountItem(10).Should().Be(120);

        int remainder = bag.TryAdd(new ItemSlot(ItemId: 10, Quantity: 100), maxStackSize: 99);
        remainder.Should().Be(22);
        bag.CountItem(10).Should().Be(198);
    }

    [Fact]
    public void TryAdd_NonStackableItems_OccupiesDistinctSlots()
    {
        InventoryContainer bag = new(capacity: 3);

        bag.TryAdd(new ItemSlot(ItemId: 500, Quantity: 1, InstanceId: 1001UL, Durability: 100f)).Should().Be(0);
        bag.TryAdd(new ItemSlot(ItemId: 500, Quantity: 1, InstanceId: 1002UL, Durability: 85f)).Should().Be(0);

        bag.OccupiedSlotCount.Should().Be(2);
        bag.GetSlot(0).InstanceId.Should().Be(1001UL);
        bag.GetSlot(1).InstanceId.Should().Be(1002UL);
    }

    [Fact]
    public void TryRemoveAndTryMoveOrSwap_UpdatesSlotsAndOccupiedCountAccurately()
    {
        InventoryContainer bag = new(capacity: 4);
        _ = bag.TryAdd(new ItemSlot(ItemId: 1, Quantity: 30), maxStackSize: 50);
        bag.SetSlot(2, new ItemSlot(ItemId: 1, Quantity: 40));

        // Merge slot 0 into slot 2 up to maxStackSize=50
        bag.TryMoveOrSwap(0, 2, maxStackSize: 50).Should().BeTrue();
        bag.GetSlot(2).Quantity.Should().Be(50);
        bag.GetSlot(0).Quantity.Should().Be(20);

        // Swap slot 0 with empty slot 3
        bag.TryMoveOrSwap(0, 3, maxStackSize: 50).Should().BeTrue();
        bag.GetSlot(0).IsEmpty.Should().BeTrue();
        bag.GetSlot(3).Quantity.Should().Be(20);

        int removed = bag.TryRemove(1, 55);
        removed.Should().Be(55);
        bag.CountItem(1).Should().Be(15);
        bag.OccupiedSlotCount.Should().Be(1);
    }
}
