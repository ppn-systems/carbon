// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using Carbon.Items.Containers;

namespace Carbon.Abstractions.World;

/// <summary>
/// Role-segregated player inventory, hotbar, crafting, forge enhancement, and drop port (ISP).
/// </summary>
public interface IWorldInventoryService
{
    /// <summary>Copies occupied inventory slots for the session bound to <paramref name="connectionId"/>.</summary>
    bool GetInventorySnapshot(ulong connectionId, List<(ushort SlotIndex, ItemSlot Slot)> output);

    /// <summary>Queues updating the selected hotbar slot and harvest tier; completes after the next tick (ADR-017).</summary>
    ValueTask<WorldMutationOutcome> SubmitSelectHotbarSlotAsync(ulong connectionId, byte slotIndex);

    /// <summary>Queues a crafting recipe on the player's general bag storage; completes after the next tick (ADR-017).</summary>
    ValueTask<WorldMutationOutcome> SubmitCraftRecipeAsync(ulong connectionId, ushort recipeId, ushort quantity);

    /// <summary>
    /// Queues moving, swapping, merging or equipping an inventory slot (<c>0..39</c>), enforcing armor slot constraints
    /// (<c>36..39</c>); completes after the next tick (ADR-017).
    /// </summary>
    ValueTask<WorldMutationOutcome> SubmitMoveInventorySlotAsync(ulong connectionId, ushort fromSlot, ushort toSlot);

    /// <summary>
    /// Queues enhancing the weapon, tool or armor piece in <paramref name="slotIndex"/> (<c>+0..+10</c>); completes after
    /// the next tick (ADR-017).
    /// </summary>
    ValueTask<WorldMutationOutcome> SubmitUpgradeInventoryItemAsync(ulong connectionId, ushort slotIndex);

    /// <summary>
    /// Queues dropping items from <paramref name="slotIndex"/> onto the ground as loot; completes after the next tick
    /// (ADR-017).
    /// </summary>
    ValueTask<WorldMutationOutcome> SubmitDropInventoryItemAsync(ulong connectionId, ushort slotIndex, int quantity);

    /// <summary>
    /// Queues buying vendor-sold items for gold while standing near a friendly town NPC; completes after the next tick
    /// (ADR-017).
    /// </summary>
    ValueTask<WorldMutationOutcome> SubmitVendorBuyAsync(ulong connectionId, ushort itemId, ushort quantity);

    /// <summary>
    /// Queues selling units from a bag slot (<c>0..35</c>) to a nearby friendly town NPC for gold; completes after the
    /// next tick (ADR-017).
    /// </summary>
    ValueTask<WorldMutationOutcome> SubmitVendorSellAsync(ulong connectionId, ushort slotIndex, ushort quantity);

    /// <summary>
    /// Queues repairing the item in <paramref name="slotIndex"/> to full durability for gold at a forge station;
    /// completes after the next tick (ADR-017).
    /// </summary>
    ValueTask<WorldMutationOutcome> SubmitRepairItemAsync(ulong connectionId, ushort slotIndex);
}
