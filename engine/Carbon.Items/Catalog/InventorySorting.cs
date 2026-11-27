// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;

namespace Carbon.Items.Catalog;

/// <summary>
/// Server-side backpack auto-sort (ADR-020 D3). Merges partial stacks of the same item, then orders the backpack
/// (<c>9..35</c>) by category, rarity (best first), item, enhancement level, durability and freshness, with empty slots
/// last. The hotbar and equipment slots are never touched. Quantity is conserved and the result is deterministic.
/// Zero heap allocations.
/// </summary>
public static class InventorySorting
{
    /// <summary>
    /// Sorts the backpack of <paramref name="inventory"/> in place.
    /// </summary>
    /// <param name="rules">Item rules (stack sizes, categories, rarities).</param>
    /// <param name="inventory">The player's inventory.</param>
    /// <returns><see langword="true"/> when any backpack slot changed.</returns>
    public static bool SortBackpack(ItemRules rules, InventoryContainer inventory)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(inventory);

        int start = PlayerInventoryLayout.BackpackStart;
        int end = Math.Min(PlayerInventoryLayout.BagLimit, inventory.Capacity);
        if (end - start <= 1)
        {
            return false;
        }

        int count = end - start;
        Span<ItemSlot> work = stackalloc ItemSlot[count];
        inventory.Slots[start..end].CopyTo(work);
        MergePartialStacks(rules, work);

        Span<SortEntry> entries = stackalloc SortEntry[count];
        for (int i = 0; i < count; i++)
        {
            entries[i] = CreateEntry(rules, in work[i]);
        }

        entries.Sort();

        bool changed = false;
        for (int i = 0; i < count; i++)
        {
            ItemSlot sorted = entries[i].Slot;
            if (inventory.GetSlot(start + i) != sorted)
            {
                inventory.SetSlot(start + i, in sorted);
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    /// Gets the sort rank of <paramref name="category"/>: gear first, then consumables, placeables and materials.
    /// </summary>
    /// <param name="category">Item category.</param>
    /// <returns>Rank, lower sorts first.</returns>
    public static int GetCategoryRank(ItemCategory category) => category switch
    {
        ItemCategory.Weapon => 0,
        ItemCategory.Tool => 1,
        ItemCategory.Armor => 2,
        ItemCategory.Accessory => 3,
        ItemCategory.Consumable => 4,
        ItemCategory.Placeable => 5,
        ItemCategory.Resource => 6,
        ItemCategory.Quest => 7,
        ItemCategory.None or _ => 8,
    };

    private static void MergePartialStacks(ItemRules rules, Span<ItemSlot> work)
    {
        for (int i = 1; i < work.Length; i++)
        {
            ItemSlot source = work[i];
            if (source.IsEmpty || !source.IsStackable)
            {
                continue;
            }

            int maxStack = rules.GetMaxStackSize(source.ItemId);
            for (int j = 0; j < i && source.Quantity > 0; j++)
            {
                ItemSlot target = work[j];
                if (target.IsEmpty || target.ItemId != source.ItemId || !target.IsStackable || target.Quantity >= maxStack)
                {
                    continue;
                }

                int moved = Math.Min(source.Quantity, maxStack - target.Quantity);
                work[j] = target.MergeWith(in source, moved);
                source = source with { Quantity = source.Quantity - moved };
            }

            work[i] = source.Quantity > 0 ? source : ItemSlot.Empty;
        }
    }

    private static SortEntry CreateEntry(ItemRules rules, in ItemSlot slot)
    {
        if (slot.IsEmpty)
        {
            return new SortEntry(int.MaxValue, 0, ItemSlot.Empty);
        }

        int categoryRank = GetCategoryRank(ItemCategory.None);
        int rarity = 0;
        if (rules.ItemCatalog.TryGet(slot.ItemId, out ItemCatalogEntry? entry))
        {
            categoryRank = GetCategoryRank(entry.Category);
            rarity = (int)entry.Rarity;
        }

        return new SortEntry(categoryRank, rarity, slot);
    }

    /// <summary>One backpack slot with its precomputed sort key; empty slots carry the largest category rank.</summary>
    private readonly record struct SortEntry(int CategoryRank, int Rarity, ItemSlot Slot) : IComparable<SortEntry>
    {
        public int CompareTo(SortEntry other)
        {
            int c = this.CategoryRank.CompareTo(other.CategoryRank);
            if (c != 0)
            {
                return c;
            }

            c = other.Rarity.CompareTo(this.Rarity);
            if (c != 0)
            {
                return c;
            }

            ItemSlot a = this.Slot;
            ItemSlot b = other.Slot;
            c = a.ItemId.CompareTo(b.ItemId);
            if (c != 0)
            {
                return c;
            }

            c = b.UpgradeLevel.CompareTo(a.UpgradeLevel);
            if (c != 0)
            {
                return c;
            }

            c = b.Durability.CompareTo(a.Durability);
            if (c != 0)
            {
                return c;
            }

            c = b.Quantity.CompareTo(a.Quantity);
            if (c != 0)
            {
                return c;
            }

            // Stalest food first, so it is eaten first (ADR-016).
            c = a.FreshnessSeconds.CompareTo(b.FreshnessSeconds);
            return c != 0 ? c : a.InstanceId.CompareTo(b.InstanceId);
        }
    }
}
