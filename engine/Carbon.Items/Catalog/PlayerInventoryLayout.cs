// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Items.Containers;
using Carbon.Items.Crafting;
using Carbon.Items.Definitions;

namespace Carbon.Items.Catalog;

/// <summary>
/// Authoritative 42-slot player inventory layout partitioning Hotbar (<c>0..8</c>), 3x9 Backpack (<c>9..35</c>),
/// 4 dedicated Armor paper-doll slots (<c>36..39</c>) and 2 Accessory slots (<c>40..41</c>, ADR-020).
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

    /// <summary>First accessory (ring/amulet) slot index (ADR-020).</summary>
    public const int Accessory1Slot = 40;

    /// <summary>Second accessory (ring/amulet) slot index (ADR-020).</summary>
    public const int Accessory2Slot = 41;

    /// <summary>Total capacity of a player's inventory (<c>42</c>).</summary>
    public const int TotalCapacity = 42;

    /// <summary>Returns the required <see cref="EquipmentSlotKind"/> for <paramref name="slotIndex"/>, or <see cref="EquipmentSlotKind.None"/> for general bag slots.</summary>
    public static EquipmentSlotKind GetRequiredEquipKind(int slotIndex) => slotIndex switch
    {
        HeadSlot => EquipmentSlotKind.Head,
        ChestSlot => EquipmentSlotKind.Chest,
        LegsSlot => EquipmentSlotKind.Legs,
        FeetSlot => EquipmentSlotKind.Feet,
        Accessory1Slot => EquipmentSlotKind.Accessory1,
        Accessory2Slot => EquipmentSlotKind.Accessory2,
        _ => EquipmentSlotKind.None,
    };

    /// <summary>Returns whether <paramref name="kind"/> is one of the two interchangeable accessory kinds.</summary>
    public static bool IsAccessoryKind(EquipmentSlotKind kind) =>
        kind is EquipmentSlotKind.Accessory1 or EquipmentSlotKind.Accessory2;

    /// <summary>
    /// Returns the equipment slot index an item of <paramref name="kind"/> goes to on quick-equip: the matching armor
    /// slot, or the first empty accessory slot (the first accessory slot when both are taken); <c>-1</c> otherwise.
    /// </summary>
    public static int GetQuickEquipSlotIndex(ReadOnlySpan<ItemSlot> slots, EquipmentSlotKind kind)
    {
        int armor = GetArmorSlotIndex(kind);
        if (armor >= 0)
        {
            return armor < slots.Length ? armor : -1;
        }

        if (!IsAccessoryKind(kind) || Accessory2Slot >= slots.Length)
        {
            return -1;
        }

        return slots[Accessory1Slot].IsEmpty || !slots[Accessory2Slot].IsEmpty ? Accessory1Slot : Accessory2Slot;
    }

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
    public static bool CanAcceptSlotItem(ItemRules rules, int slotIndex, in ItemSlot slot)
    {
        ArgumentNullException.ThrowIfNull(rules);
        EquipmentSlotKind required = GetRequiredEquipKind(slotIndex);
        if (required == EquipmentSlotKind.None || slot.IsEmpty)
        {
            return true;
        }

        if (!rules.Items.TryGet(slot.ItemId, out ItemArchetype? def))
        {
            return false;
        }

        // Rings and amulets fit either accessory slot.
        return def.EquipSlot == required || (IsAccessoryKind(required) && IsAccessoryKind(def.EquipSlot));
    }

    /// <summary>
    /// Moves, merges, swaps, or equips between <paramref name="fromSlot"/> and <paramref name="toSlot"/>
    /// while strictly enforcing armor slot constraints (<c>36..39</c>).
    /// </summary>
    public static bool TryMoveOrEquipSlot(ItemRules rules, InventoryContainer inventory, int fromSlot, int toSlot)
    {
        ArgumentNullException.ThrowIfNull(rules);
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
        if (!CanAcceptSlotItem(rules, toSlot, in source) || !CanAcceptSlotItem(rules, fromSlot, in target))
        {
            return false;
        }

        int maxStack = rules.GetMaxStackSize(source.ItemId);
        return inventory.TryMoveOrSwap(fromSlot, toSlot, maxStack);
    }

    /// <summary>
    /// Quick-moves or equips an item on right-click (armor to/from dedicated armor slot, backpack to/from hotbar).
    /// </summary>
    public static bool TryQuickMove(ItemRules rules, InventoryContainer inventory, int slotIndex, out int destinationSlot)
    {
        ArgumentNullException.ThrowIfNull(rules);
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

        // 1. Armor piece or accessory inside bag -> equip directly into its equipment slot.
        if (slotIndex < BagLimit &&
            rules.Items.TryGet(source.ItemId, out ItemArchetype? def) &&
            GetQuickEquipSlotIndex(inventory.Slots, def.EquipSlot) is int armorSlot &&
            armorSlot >= 0 &&
            TryMoveOrEquipSlot(rules, inventory, slotIndex, armorSlot))
        {
            destinationSlot = armorSlot;
            return true;
        }

        // 2. From an equipment slot -> first open bag slot (9..35 then 0..8).
        if (slotIndex >= BagLimit)
        {
            int targetBag = FindFirstEmptyOrMergeSlot(rules, inventory, in source, BackpackStart, Math.Min(BagLimit, inventory.Capacity));
            if (targetBag < 0)
            {
                targetBag = FindFirstEmptyOrMergeSlot(rules, inventory, in source, 0, Math.Min(HotbarCount, inventory.Capacity));
            }

            if (targetBag >= 0 && TryMoveOrEquipSlot(rules, inventory, slotIndex, targetBag))
            {
                destinationSlot = targetBag;
                return true;
            }

            return false;
        }

        // 3. Hotbar <-> Backpack
        int start = slotIndex < HotbarCount ? BackpackStart : 0;
        int end = slotIndex < HotbarCount ? Math.Min(BagLimit, inventory.Capacity) : Math.Min(HotbarCount, inventory.Capacity);
        int target = FindFirstEmptyOrMergeSlot(rules, inventory, in source, start, end);
        if (target >= 0 && TryMoveOrEquipSlot(rules, inventory, slotIndex, target))
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
                    inventory.SetSlot(i, s.MergeWith(in incoming, add));
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

        // Items that cannot stack take one slot per unit (as InventoryDraft.Add does).
        for (int i = 0; i < bagCap && remaining > 0; i++)
        {
            if (inventory.GetSlot(i).IsEmpty)
            {
                inventory.SetSlot(i, incoming with { Quantity = 1 });
                remaining--;
            }
        }

        return remaining;
    }

    /// <summary>
    /// Moves <paramref name="quantity"/> units of the stack in bag slot <paramref name="fromSlot"/> into bag slot
    /// <paramref name="toSlot"/>, which must be empty or hold the same stackable item with room for all of them.
    /// <paramref name="quantity"/> must leave at least one unit behind (moving the whole stack is a move).
    /// Quantity is conserved: nothing is created or destroyed.
    /// </summary>
    /// <returns><see langword="true"/> when the split was applied; otherwise nothing changed.</returns>
    public static bool TrySplitStack(ItemRules rules, InventoryContainer inventory, int fromSlot, int toSlot, int quantity)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(inventory);
        int bagCap = Math.Min(BagLimit, inventory.Capacity);
        if (fromSlot == toSlot || (uint)fromSlot >= (uint)bagCap || (uint)toSlot >= (uint)bagCap)
        {
            return false;
        }

        ItemSlot source = inventory.GetSlot(fromSlot);
        if (source.IsEmpty || !source.IsStackable || quantity <= 0 || quantity >= source.Quantity)
        {
            return false;
        }

        ItemSlot target = inventory.GetSlot(toSlot);
        ItemSlot merged;
        if (target.IsEmpty)
        {
            merged = source with { Quantity = quantity };
        }
        else if (target.ItemId == source.ItemId &&
                 target.IsStackable &&
                 target.Quantity + quantity <= rules.GetMaxStackSize(source.ItemId))
        {
            merged = target.MergeWith(in source, quantity);
        }
        else
        {
            return false;
        }

        inventory.SetSlot(fromSlot, source with { Quantity = source.Quantity - quantity });
        inventory.SetSlot(toSlot, in merged);
        return true;
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

    private static int FindFirstEmptyOrMergeSlot(ItemRules rules, InventoryContainer inventory, in ItemSlot source, int startInclusive, int endExclusive)
    {
        if (source.IsStackable)
        {
            int maxStack = rules.GetMaxStackSize(source.ItemId);
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
