// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;

namespace Carbon.Desktop.Abstractions.World;

/// <summary>
/// Client-side inventory UX rules (ADR-020 D7): which actions ask for confirmation, where a split lands, and how
/// hover-to-bind maps to a move. The server re-validates every request; these rules only shape the UI.
/// </summary>
public static class InventoryActionPolicy
{
    /// <summary>
    /// Returns the bag slot a split of <paramref name="fromSlot"/> goes to: the first empty backpack slot, then the first
    /// empty hotbar slot; <c>-1</c> when the bag is full.
    /// </summary>
    public static int FindSplitTargetSlot(ReadOnlySpan<ItemSlot> slots, int fromSlot)
    {
        int bagEnd = Math.Min(PlayerInventoryLayout.BagLimit, slots.Length);
        for (int i = PlayerInventoryLayout.BackpackStart; i < bagEnd; i++)
        {
            if (i != fromSlot && slots[i].IsEmpty)
            {
                return i;
            }
        }

        int hotbarEnd = Math.Min(PlayerInventoryLayout.HotbarCount, slots.Length);
        for (int i = 0; i < hotbarEnd; i++)
        {
            if (i != fromSlot && slots[i].IsEmpty)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Returns whether <paramref name="slot"/> can be split: a stackable stack of at least two in the bag.</summary>
    public static bool CanSplit(in ItemSlot slot, int slotIndex) =>
        slotIndex >= 0 && slotIndex < PlayerInventoryLayout.BagLimit && !slot.IsEmpty && slot.IsStackable && slot.Quantity >= 2;

    /// <summary>Returns the split amount a dialog starts at: half the stack, rounded down, at least one.</summary>
    public static int DefaultSplitQuantity(int stackQuantity) => Math.Max(1, stackQuantity / 2);

    /// <summary>Clamps a split amount to <c>1..stack-1</c>.</summary>
    public static int ClampSplitQuantity(int quantity, int stackQuantity) =>
        Math.Clamp(quantity, 1, Math.Max(1, stackQuantity - 1));

    /// <summary>
    /// Returns whether dropping <paramref name="slot"/> from <paramref name="slotIndex"/> asks first: rare or better,
    /// enhanced, or equipped items. Destroying always asks.
    /// </summary>
    public static bool RequiresDropConfirmation(ItemRules rules, in ItemSlot slot, int slotIndex)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (slot.IsEmpty)
        {
            return false;
        }

        if (slot.UpgradeLevel > 0 || slotIndex >= PlayerInventoryLayout.BagLimit)
        {
            return true;
        }

        return rules.ItemCatalog.TryGet(slot.ItemId, out ItemCatalogEntry? entry) && entry.Rarity >= ItemRarity.Rare;
    }

    /// <summary>
    /// Resolves "hover a slot and press 1..9": move the hovered item onto hotbar slot <paramref name="hotbarIndex"/>
    /// (the server swaps when it is taken). Returns <see langword="false"/> when there is nothing to do.
    /// </summary>
    public static bool TryResolveHotbarBind(ReadOnlySpan<ItemSlot> slots, int hoveredSlot, int hotbarIndex, out ushort fromSlot, out ushort toSlot)
    {
        fromSlot = 0;
        toSlot = 0;
        if (hotbarIndex < 0 || hotbarIndex >= PlayerInventoryLayout.HotbarCount ||
            hoveredSlot < 0 || hoveredSlot >= slots.Length || hoveredSlot == hotbarIndex || slots[hoveredSlot].IsEmpty)
        {
            return false;
        }

        fromSlot = (ushort)hoveredSlot;
        toSlot = (ushort)hotbarIndex;
        return true;
    }
}
