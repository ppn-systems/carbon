// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Simulation.Items;

/// <summary>
/// Unmanaged value representation of an item entry inside an inventory or equipment slot.
/// </summary>
/// <param name="ItemId">Item definition identifier (0 represents an empty slot).</param>
/// <param name="Quantity">Number of items in the slot.</param>
/// <param name="InstanceId">Optional unique instance identifier for non-stackable equipment/items (0 for stackable items).</param>
/// <param name="Durability">Optional remaining item durability.</param>
public readonly record struct ItemSlot(
    uint ItemId,
    int Quantity,
    ulong InstanceId = 0,
    float Durability = 0f)
{
    /// <summary>
    /// Gets an empty item slot.
    /// </summary>
    public static ItemSlot Empty => default;

    /// <summary>
    /// Gets a value indicating whether this slot contains no items.
    /// </summary>
    public bool IsEmpty => this.ItemId == 0 || this.Quantity <= 0;

    /// <summary>
    /// Gets a value indicating whether items in this slot can merge with matching <see cref="ItemId"/> entries.
    /// </summary>
    public bool IsStackable => this.InstanceId == 0;
}
