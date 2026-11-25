// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Data.Registry;
using Carbon.Items.Definitions;

namespace Carbon.Items.Containers;

/// <summary>
/// Fixed-capacity slot-based inventory container storing unmanaged <see cref="ItemSlot"/> entries.
/// Provides deterministic stacking, splitting, removal, and slot swap/merge operations with zero heap allocations.
/// </summary>
public sealed class InventoryContainer
{
    private readonly ItemSlot[] _slots;

    /// <summary>
    /// Gets the total number of slots in this container.
    /// </summary>
    public int Capacity => _slots.Length;

    /// <summary>
    /// Gets the number of non-empty slots currently occupied.
    /// </summary>
    public int OccupiedSlotCount { get; private set; }

    /// <summary>
    /// Gets a read-only span over all inventory slots.
    /// </summary>
    public ReadOnlySpan<ItemSlot> Slots => _slots;

    /// <summary>
    /// Initializes a new instance of the <see cref="InventoryContainer"/> class.
    /// </summary>
    /// <param name="capacity">Fixed number of inventory slots.</param>
    public InventoryContainer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _slots = new ItemSlot[capacity];
    }

    /// <summary>
    /// Gets the <see cref="ItemSlot"/> stored at <paramref name="slotIndex"/>.
    /// </summary>
    /// <param name="slotIndex">Zero-based slot index.</param>
    /// <returns>The stored item entry.</returns>
    public ItemSlot GetSlot(int slotIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slotIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slotIndex, _slots.Length);
        return _slots[slotIndex];
    }

    /// <summary>
    /// Directly assigns the <see cref="ItemSlot"/> at <paramref name="slotIndex"/>.
    /// </summary>
    /// <param name="slotIndex">Zero-based slot index.</param>
    /// <param name="entry">The item entry to write (empty or zero-quantity entries clear the slot).</param>
    public void SetSlot(int slotIndex, in ItemSlot entry)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slotIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slotIndex, _slots.Length);
        this.SetSlotInternal(slotIndex, entry.IsEmpty ? ItemSlot.Empty : entry);
    }

    /// <summary>
    /// Attempts to add <paramref name="incoming"/> using the max stack size configured on its <see cref="ItemArchetype"/>.
    /// </summary>
    /// <param name="incoming">Item entry to insert.</param>
    /// <param name="registry">Item archetype registry.</param>
    /// <returns>The unplaced remainder quantity (0 if the entire entry fit).</returns>
    public int TryAdd(in ItemSlot incoming, DefinitionRegistry<uint, ItemArchetype> registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        if (incoming.IsEmpty)
        {
            return 0;
        }

        int maxStack = registry.TryGet(incoming.ItemId, out ItemArchetype? archetype)
            ? Math.Max(1, archetype.MaxStackSize)
            : 99;
        return this.TryAdd(in incoming, maxStack);
    }

    /// <summary>
    /// Attempts to add <paramref name="incoming"/> into the container, merging with existing matching stackable
    /// slots before allocating empty slots.
    /// </summary>
    /// <param name="incoming">Item entry to insert.</param>
    /// <param name="maxStackSize">Maximum quantity permitted per stackable slot.</param>
    /// <returns>The unplaced remainder quantity (0 if the entire entry fit).</returns>
    public int TryAdd(in ItemSlot incoming, int maxStackSize = 99)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxStackSize);

        if (incoming.IsEmpty)
        {
            return 0;
        }

        int remaining = incoming.Quantity;

        if (incoming.IsStackable)
        {
            for (int i = 0; i < _slots.Length && remaining > 0; i++)
            {
                ItemSlot slot = _slots[i];
                if (slot.ItemId == incoming.ItemId && slot.IsStackable && slot.Quantity < maxStackSize)
                {
                    int add = Math.Min(remaining, maxStackSize - slot.Quantity);
                    this.SetSlotInternal(i, slot with { Quantity = slot.Quantity + add });
                    remaining -= add;
                }
            }

            for (int i = 0; i < _slots.Length && remaining > 0; i++)
            {
                if (_slots[i].IsEmpty)
                {
                    int add = Math.Min(remaining, maxStackSize);
                    this.SetSlotInternal(i, incoming with { Quantity = add });
                    remaining -= add;
                }
            }

            return remaining;
        }

        for (int i = 0; i < _slots.Length && remaining > 0; i++)
        {
            if (_slots[i].IsEmpty)
            {
                this.SetSlotInternal(i, incoming);
                remaining = 0;
            }
        }

        return remaining;
    }

    /// <summary>
    /// Attempts to remove up to <paramref name="quantity"/> units of <paramref name="itemId"/> across container slots.
    /// </summary>
    /// <param name="itemId">Item definition identifier to remove.</param>
    /// <param name="quantity">Requested quantity to remove.</param>
    /// <returns>The actual quantity removed.</returns>
    public int TryRemove(uint itemId, int quantity)
    {
        if (itemId == 0 || quantity <= 0)
        {
            return 0;
        }

        int needed = quantity;
        for (int i = 0; i < _slots.Length && needed > 0; i++)
        {
            ItemSlot slot = _slots[i];
            if (slot.ItemId != itemId || slot.Quantity <= 0)
            {
                continue;
            }

            int take = Math.Min(needed, slot.Quantity);
            int left = slot.Quantity - take;
            this.SetSlotInternal(i, left > 0 ? (slot with { Quantity = left }) : ItemSlot.Empty);
            needed -= take;
        }

        return quantity - needed;
    }

    /// <summary>
    /// Moves items from <paramref name="fromSlot"/> to <paramref name="toSlot"/>, merging stackable items of the same
    /// type or swapping the two slots otherwise.
    /// </summary>
    /// <param name="fromSlot">Source slot index.</param>
    /// <param name="toSlot">Destination slot index.</param>
    /// <param name="maxStackSize">Maximum quantity permitted per stackable slot.</param>
    /// <returns><see langword="true"/> if the slots changed; otherwise, <see langword="false"/>.</returns>
    public bool TryMoveOrSwap(int fromSlot, int toSlot, int maxStackSize = 99)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fromSlot);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(fromSlot, _slots.Length);
        ArgumentOutOfRangeException.ThrowIfNegative(toSlot);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(toSlot, _slots.Length);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxStackSize);

        if (fromSlot == toSlot || _slots[fromSlot].IsEmpty)
        {
            return false;
        }

        ItemSlot source = _slots[fromSlot];
        ItemSlot target = _slots[toSlot];

        if (target.IsEmpty)
        {
            this.SetSlotInternal(toSlot, source);
            this.SetSlotInternal(fromSlot, ItemSlot.Empty);
            return true;
        }

        if (source.IsStackable && target.IsStackable && source.ItemId == target.ItemId && target.Quantity < maxStackSize)
        {
            int transfer = Math.Min(source.Quantity, maxStackSize - target.Quantity);
            this.SetSlotInternal(toSlot, target with { Quantity = target.Quantity + transfer });
            int remainder = source.Quantity - transfer;
            this.SetSlotInternal(fromSlot, remainder > 0 ? (source with { Quantity = remainder }) : ItemSlot.Empty);
            return true;
        }

        this.SetSlotInternal(toSlot, source);
        this.SetSlotInternal(fromSlot, target);
        return true;
    }

    /// <summary>
    /// Counts the total quantity of <paramref name="itemId"/> across all slots.
    /// </summary>
    /// <param name="itemId">Item definition identifier.</param>
    /// <returns>Total quantity stored in this container.</returns>
    public int CountItem(uint itemId)
    {
        if (itemId == 0)
        {
            return 0;
        }

        int total = 0;
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i].ItemId == itemId && _slots[i].Quantity > 0)
            {
                total += _slots[i].Quantity;
            }
        }

        return total;
    }

    /// <summary>
    /// Clears every slot in this container.
    /// </summary>
    public void Clear()
    {
        Array.Clear(_slots);
        this.OccupiedSlotCount = 0;
    }

    private void SetSlotInternal(int slotIndex, in ItemSlot entry)
    {
        bool wasOccupied = !_slots[slotIndex].IsEmpty;
        bool isOccupied = !entry.IsEmpty;

        _slots[slotIndex] = entry;

        if (!wasOccupied && isOccupied)
        {
            this.OccupiedSlotCount++;
        }
        else if (wasOccupied && !isOccupied)
        {
            this.OccupiedSlotCount--;
        }
    }
}
