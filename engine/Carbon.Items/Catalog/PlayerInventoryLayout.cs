// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Items.Containers;
using Carbon.Items.Crafting;
using Carbon.Items.Definitions;

namespace Carbon.Items.Catalog;

/// <summary>
/// Authoritative 40-slot Minecraft-style player inventory layout partitioning Hotbar (<c>0..8</c>),
/// 3x9 Backpack (<c>9..35</c>), and 4 dedicated Armor paper-doll slots (<c>36..39</c>).
/// </summary>
public static class PlayerInventoryLayout
{
    /// <summary>Hotbar slot count (<c>0..8</c>).</summary>
    public const int HotbarCount = 9;

    /// <summary>First backpack slot index (<c>9</c>).</summary>
    public const int BackpackStart = 9;

    /// <summary>3x9 backpack slot count (<c>9..35</c>).</summary>
    public const int BackpackCount = 27;

    /// <summary>Exclusive end of bag storage (hotbar + backpack = 36 slots).</summary>
    public const int BagLimit = 36;

    /// <summary>Dedicated Head armor slot index.</summary>
    public const int HeadSlot = 36;

    /// <summary>Dedicated Chest armor slot index.</summary>
    public const int ChestSlot = 37;

    /// <summary>Dedicated Legs armor slot index.</summary>
    public const int LegsSlot = 38;

    /// <summary>Dedicated Feet armor slot index.</summary>
    public const int FeetSlot = 39;

    /// <summary>Total capacity of a player's inventory (<c>40</c>).</summary>
    public const int TotalCapacity = 40;

    /// <summary>Returns the required <see cref="EquipmentSlotKind"/> for <paramref name="slotIndex"/>, or <see cref="EquipmentSlotKind.None"/> for general bag slots.</summary>
    public static EquipmentSlotKind GetRequiredEquipKind(int slotIndex) => slotIndex switch
    {
        HeadSlot => EquipmentSlotKind.Head,
        ChestSlot => EquipmentSlotKind.Chest,
        LegsSlot => EquipmentSlotKind.Legs,
        FeetSlot => EquipmentSlotKind.Feet,
        _ => EquipmentSlotKind.None,
    };

    /// <summary>Returns the dedicated armor slot index for <paramref name="kind"/>, or <c>-1</c> if non-armor.</summary>
    public static int GetArmorSlotIndex(EquipmentSlotKind kind) => kind switch
    {
        EquipmentSlotKind.Head => HeadSlot,
        EquipmentSlotKind.Chest => ChestSlot,
        EquipmentSlotKind.Legs => LegsSlot,
        EquipmentSlotKind.Feet => FeetSlot,
        EquipmentSlotKind.None
        or EquipmentSlotKind.MainHand
        or EquipmentSlotKind.OffHand
        or EquipmentSlotKind.Tool
        or EquipmentSlotKind.Accessory1
        or EquipmentSlotKind.Accessory2
        or _ => -1,
    };

    /// <summary>Validates whether <paramref name="slot"/> may occupy <paramref name="slotIndex"/>.</summary>
    public static bool CanAcceptSlotItem(int slotIndex, in ItemSlot slot)
    {
        EquipmentSlotKind required = GetRequiredEquipKind(slotIndex);
        if (required == EquipmentSlotKind.None || slot.IsEmpty)
        {
            return true;
        }

        return StarterGameCatalog.Items.TryGet(slot.ItemId, out ItemArchetype? def) && def.EquipSlot == required;
    }

    /// <summary>
    /// Moves, merges, swaps, or equips between <paramref name="fromSlot"/> and <paramref name="toSlot"/>
    /// while strictly enforcing armor slot constraints (<c>36..39</c>).
    /// </summary>
    public static bool TryMoveOrEquipSlot(InventoryContainer inventory, int fromSlot, int toSlot)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        if (fromSlot == toSlot ||
            (uint)fromSlot >= (uint)inventory.Capacity ||
            (uint)toSlot >= (uint)inventory.Capacity)
        {
            return false;
        }

        ItemSlot source = inventory.GetSlot(fromSlot);
        if (source.IsEmpty)
        {
            return false;
        }

        ItemSlot target = inventory.GetSlot(toSlot);
        if (!CanAcceptSlotItem(toSlot, in source) || !CanAcceptSlotItem(fromSlot, in target))
        {
            return false;
        }

        int maxStack = StarterGameCatalog.GetMaxStackSize(source.ItemId);
        return inventory.TryMoveOrSwap(fromSlot, toSlot, maxStack);
    }

    /// <summary>
    /// Quick-moves or equips an item on right-click (armor to/from dedicated armor slot, backpack to/from hotbar).
    /// </summary>
    public static bool TryQuickMove(InventoryContainer inventory, int slotIndex, out int destinationSlot)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        destinationSlot = -1;
        if ((uint)slotIndex >= (uint)inventory.Capacity)
        {
            return false;
        }

        ItemSlot source = inventory.GetSlot(slotIndex);
        if (source.IsEmpty)
        {
            return false;
        }

        // 1. Armor piece inside bag -> equip directly into matching armor slot.
        if (slotIndex < BagLimit &&
            StarterGameCatalog.Items.TryGet(source.ItemId, out ItemArchetype? def) &&
            GetArmorSlotIndex(def.EquipSlot) is int armorSlot &&
            armorSlot >= 0 &&
            armorSlot < inventory.Capacity &&
            TryMoveOrEquipSlot(inventory, slotIndex, armorSlot))
        {
            destinationSlot = armorSlot;
            return true;
        }

        // 2. From armor slot -> first open bag slot (9..35 then 0..8).
        if (slotIndex >= BagLimit)
        {
            int targetBag = FindFirstEmptyOrMergeSlot(inventory, in source, BackpackStart, Math.Min(BagLimit, inventory.Capacity));
            if (targetBag < 0)
            {
                targetBag = FindFirstEmptyOrMergeSlot(inventory, in source, 0, Math.Min(HotbarCount, inventory.Capacity));
            }

            if (targetBag >= 0 && TryMoveOrEquipSlot(inventory, slotIndex, targetBag))
            {
                destinationSlot = targetBag;
                return true;
            }

            return false;
        }

        // 3. Hotbar <-> Backpack
        int start = slotIndex < HotbarCount ? BackpackStart : 0;
        int end = slotIndex < HotbarCount ? Math.Min(BagLimit, inventory.Capacity) : Math.Min(HotbarCount, inventory.Capacity);
        int target = FindFirstEmptyOrMergeSlot(inventory, in source, start, end);
        if (target >= 0 && TryMoveOrEquipSlot(inventory, slotIndex, target))
        {
            destinationSlot = target;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Inserts or merges <paramref name="incoming"/> strictly into bag slots (<c>0..35</c>),
    /// never spilling into equipped armor slots (<c>36..39</c>).
    /// </summary>
    public static int TryAddBagItem(InventoryContainer inventory, in ItemSlot incoming, int maxStackSize)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        if (incoming.IsEmpty)
        {
            return 0;
        }

        int bagCap = Math.Min(BagLimit, inventory.Capacity);
        int remaining = incoming.Quantity;

        if (maxStackSize > 1 && incoming.IsStackable)
        {
            for (int i = 0; i < bagCap && remaining > 0; i++)
            {
                ItemSlot s = inventory.GetSlot(i);
                if (s.ItemId == incoming.ItemId && s.IsStackable && s.Quantity < maxStackSize)
                {
                    int add = Math.Min(remaining, maxStackSize - s.Quantity);
                    inventory.SetSlot(i, s with { Quantity = s.Quantity + add });
                    remaining -= add;
                }
            }

            for (int i = 0; i < bagCap && remaining > 0; i++)
            {
                if (inventory.GetSlot(i).IsEmpty)
                {
                    int add = Math.Min(remaining, maxStackSize);
                    inventory.SetSlot(i, incoming with { Quantity = add });
                    remaining -= add;
                }
            }

            return remaining;
        }

        for (int i = 0; i < bagCap && remaining > 0; i++)
        {
            if (inventory.GetSlot(i).IsEmpty)
            {
                inventory.SetSlot(i, incoming);
                remaining = 0;
            }
        }

        return remaining;
    }

    /// <summary>
    /// Crafts <paramref name="recipe"/> using only general bag/hotbar storage (<c>0..35</c>) on non-upgraded
    /// slots (<c>UpgradeLevel == 0</c>) so outputs and deductions never touch equipped or enhanced gear.
    /// </summary>
    public static bool TryCraftInBag(InventoryContainer inventory, CraftingRecipe recipe, int maxOutputStackSize = 99)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxOutputStackSize);

        // All-or-nothing: staged on a draft of the bag slots and committed only if every output fits.
        return RecipeMatcher.TryCraft(
            inventory,
            recipe,
            new FixedStackSizeProvider(maxOutputStackSize),
            CraftContext.Unrestricted,
            Math.Min(BagLimit, inventory.Capacity)).IsOk;
    }

    /// <summary>Counts total un-upgraded stacked units of <paramref name="itemId"/> across bag storage slots (<c>0..BagLimit-1</c>).</summary>
    public static int CountItem(ReadOnlySpan<ItemSlot> slots, uint itemId)
    {
        int total = 0;
        int bagCount = Math.Min(BagLimit, slots.Length);
        for (int i = 0; i < bagCount; i++)
        {
            if (slots[i].ItemId == itemId && slots[i].UpgradeLevel == 0 && slots[i].Quantity > 0)
            {
                total += slots[i].Quantity;
            }
        }

        return total;
    }

    /// <summary>Returns whether <paramref name="slots"/> contains enough ingredients to craft <paramref name="quantity"/> copies of <paramref name="recipe"/>.</summary>
    public static bool CanCraft(ReadOnlySpan<ItemSlot> slots, CraftingRecipe recipe, int quantity = 1)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        if (quantity <= 0)
        {
            return false;
        }

        for (int i = 0; i < recipe.Ingredients.Count; i++)
        {
            RecipeIngredient ing = recipe.Ingredients[i];
            if (CountItem(slots, ing.ItemId) < ing.Quantity * quantity)
            {
                return false;
            }
        }

        return true;
    }

    private static int FindFirstEmptyOrMergeSlot(InventoryContainer inventory, in ItemSlot source, int startInclusive, int endExclusive)
    {
        if (source.IsStackable)
        {
            int maxStack = StarterGameCatalog.GetMaxStackSize(source.ItemId);
            for (int i = startInclusive; i < endExclusive; i++)
            {
                ItemSlot candidate = inventory.GetSlot(i);
                if (candidate.ItemId == source.ItemId && candidate.IsStackable && candidate.Quantity < maxStack)
                {
                    return i;
                }
            }
        }

        for (int i = startInclusive; i < endExclusive; i++)
        {
            if (inventory.GetSlot(i).IsEmpty)
            {
                return i;
            }
        }

        return -1;
    }
}
