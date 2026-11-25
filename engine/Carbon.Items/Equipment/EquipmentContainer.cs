// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Data.Registry;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;

namespace Carbon.Items.Equipment;

/// <summary>
/// Manages an actor's equipped items indexed by <see cref="EquipmentSlotKind"/>, enforcing slot compatibility
/// and querying equipped harvesting tier.
/// </summary>
public sealed class EquipmentContainer
{
    private const int SlotCount = (int)EquipmentSlotKind.Accessory2 + 1;

    private readonly ItemSlot[] _slots = new ItemSlot[SlotCount];

    /// <summary>
    /// Gets the item currently equipped in <paramref name="slot"/>.
    /// </summary>
    /// <param name="slot">Target equipment slot.</param>
    /// <returns>The equipped <see cref="ItemSlot"/>, or <see cref="ItemSlot.Empty"/>.</returns>
    public ItemSlot GetEquipped(EquipmentSlotKind slot)
    {
        int idx = (int)slot;
        return idx > 0 && idx < _slots.Length ? _slots[idx] : ItemSlot.Empty;
    }

    /// <summary>
    /// Attempts to equip <paramref name="item"/> into <paramref name="targetSlot"/> if permitted by <paramref name="archetype"/>.
    /// </summary>
    /// <param name="targetSlot">Destination equipment slot.</param>
    /// <param name="item">Item entry to equip.</param>
    /// <param name="archetype">Item archetype validating slot compatibility.</param>
    /// <param name="previousItem">Previously equipped item swapped out, or <see cref="ItemSlot.Empty"/>.</param>
    /// <returns><see langword="true"/> if equipped; otherwise, <see langword="false"/>.</returns>
    public bool TryEquip(
        EquipmentSlotKind targetSlot,
        in ItemSlot item,
        ItemArchetype archetype,
        out ItemSlot previousItem)
    {
        ArgumentNullException.ThrowIfNull(archetype);

        if (targetSlot == EquipmentSlotKind.None || item.IsEmpty || archetype.Id != item.ItemId || archetype.EquipSlot != targetSlot)
        {
            previousItem = ItemSlot.Empty;
            return false;
        }

        int idx = (int)targetSlot;
        previousItem = _slots[idx];
        _slots[idx] = item;
        return true;
    }

    /// <summary>
    /// Removes and returns the item equipped in <paramref name="targetSlot"/>.
    /// </summary>
    /// <param name="targetSlot">Target equipment slot.</param>
    /// <param name="removedItem">The unequipped item entry.</param>
    /// <returns><see langword="true"/> if an item was present and removed; otherwise, <see langword="false"/>.</returns>
    public bool TryUnequip(EquipmentSlotKind targetSlot, out ItemSlot removedItem)
    {
        int idx = (int)targetSlot;
        if (idx <= 0 || idx >= _slots.Length || _slots[idx].IsEmpty)
        {
            removedItem = ItemSlot.Empty;
            return false;
        }

        removedItem = _slots[idx];
        _slots[idx] = ItemSlot.Empty;
        return true;
    }

    /// <summary>
    /// Computes the highest harvesting tool tier across currently equipped items.
    /// </summary>
    /// <param name="registry">Item archetype registry.</param>
    /// <returns>Maximum equipped <see cref="ItemArchetype.HarvestTier"/> (0 when none equipped).</returns>
    public byte GetMaxHarvestTier(DefinitionRegistry<uint, ItemArchetype> registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        byte maxTier = 0;
        for (int i = 1; i < _slots.Length; i++)
        {
            ItemSlot slot = _slots[i];
            if (!slot.IsEmpty && registry.TryGet(slot.ItemId, out ItemArchetype? archetype) && archetype.HarvestTier > maxTier)
            {
                maxTier = archetype.HarvestTier;
            }
        }

        return maxTier;
    }

    /// <summary>
    /// Clears all equipment slots.
    /// </summary>
    public void Clear() => Array.Clear(_slots);
}
