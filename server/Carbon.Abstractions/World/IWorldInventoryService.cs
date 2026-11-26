// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Carbon.Contracts.Common;
using Carbon.Items.Containers;

namespace Carbon.Abstractions.World;

/// <summary>
/// Role-segregated player inventory, hotbar, crafting, forge enhancement, and drop port (ISP).
/// </summary>
public interface IWorldInventoryService
{
    /// <summary>Copies occupied inventory slots for the session bound to <paramref name="connectionId"/>.</summary>
    bool GetInventorySnapshot(ulong connectionId, List<(ushort SlotIndex, ItemSlot Slot)> output);

    /// <summary>Updates the player's selected hotbar slot and harvest tier.</summary>
    OpStatus TrySelectHotbarSlot(ulong connectionId, byte slotIndex);

    /// <summary>Validates and executes a crafting recipe request on the player's general bag storage.</summary>
    OpStatus TryCraftRecipe(ulong connectionId, ushort recipeId, ushort quantity);

    /// <summary>Moves, swaps, merges, or equips an inventory slot (<c>0..39</c>), enforcing armor slot constraints (<c>36..39</c>).</summary>
    OpStatus TryMoveInventorySlot(ulong connectionId, ushort fromSlot, ushort toSlot);

    /// <summary>Attempts to enhance an eligible weapon, tool, or armor piece in <paramref name="slotIndex"/> (<c>+0..+10</c>).</summary>
    OpStatus TryUpgradeInventoryItem(ulong connectionId, ushort slotIndex);

    /// <summary>Drops items from <paramref name="slotIndex"/> onto the ground as an authoritative loot entity.</summary>
    OpStatus TryDropInventoryItem(ulong connectionId, ushort slotIndex, int quantity);

    /// <summary>Buys vendor-sold items for gold while standing near a friendly town NPC.</summary>
    OpStatus TryVendorBuy(ulong connectionId, ushort itemId, ushort quantity);

    /// <summary>Sells units from a bag slot (<c>0..35</c>) to a nearby friendly town NPC for gold.</summary>
    OpStatus TryVendorSell(ulong connectionId, ushort slotIndex, ushort quantity);

    /// <summary>Repairs the item in <paramref name="slotIndex"/> to full durability for gold at a forge station.</summary>
    OpStatus TryRepairItem(ulong connectionId, ushort slotIndex);
}
