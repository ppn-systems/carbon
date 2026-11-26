// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Items.Containers;

/// <summary>
/// Unmanaged value representation of an item entry inside an inventory or equipment slot.
/// </summary>
/// <param name="ItemId">Item definition identifier (0 represents an empty slot).</param>
/// <param name="Quantity">Number of items in the slot.</param>
/// <param name="InstanceId">Optional unique instance identifier for non-stackable equipment/items (0 for stackable items).</param>
/// <param name="Durability">Optional remaining item durability.</param>
/// <param name="UpgradeLevel">Optional forge enhancement level (<c>0..10</c>).</param>
/// <param name="FreshnessSeconds">
/// Seconds until a perishable food spoils (ADR-016). <c>0</c> means not perishable, or perishable but not yet
/// stamped by the server's spoilage tick.
/// </param>
public readonly record struct ItemSlot(
    uint ItemId,
    int Quantity,
    ulong InstanceId = 0,
    float Durability = 0f,
    byte UpgradeLevel = 0,
    float FreshnessSeconds = 0f)
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
    public bool IsStackable => this.InstanceId == 0 && this.UpgradeLevel == 0;

    /// <summary>
    /// Gets a value indicating whether this slot carries a spoilage countdown.
    /// </summary>
    public bool IsPerishable => this.FreshnessSeconds > 0f;

    /// <summary>
    /// Combines the freshness of two stacks being merged: the stack keeps the stalest (lowest) countdown, and a
    /// zero (not stamped) countdown takes the other stack's value.
    /// </summary>
    /// <param name="a">Freshness of the first stack.</param>
    /// <param name="b">Freshness of the second stack.</param>
    /// <returns>The merged freshness.</returns>
    public static float MergeFreshness(float a, float b) =>
        a <= 0f ? MathF.Max(0f, b) : b <= 0f ? a : MathF.Min(a, b);

    /// <summary>
    /// Returns this stack with <paramref name="addQuantity"/> units of <paramref name="incoming"/> merged in.
    /// </summary>
    /// <param name="incoming">Stack being merged in (same item id).</param>
    /// <param name="addQuantity">Units added.</param>
    /// <returns>The merged stack.</returns>
    public ItemSlot MergeWith(in ItemSlot incoming, int addQuantity) => this with
    {
        Quantity = this.Quantity + addQuantity,
        FreshnessSeconds = MergeFreshness(this.FreshnessSeconds, incoming.FreshnessSeconds),
    };
}
