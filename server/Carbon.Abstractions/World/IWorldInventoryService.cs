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
    /// Queues moving, swapping, merging or equipping an inventory slot (<c>0..41</c>), enforcing armor and accessory slot
    /// constraints (<c>36..41</c>); completes after the next tick (ADR-017). A non-zero <paramref name="expectedItemId"/>
    /// that no longer matches the source slot is <c>Conflict</c> (ADR-020 D2).
    /// </summary>
    ValueTask<WorldMutationOutcome> SubmitMoveInventorySlotAsync(ulong connectionId, ushort fromSlot, ushort toSlot, ushort expectedItemId);

    /// <summary>
    /// Queues moving <paramref name="quantity"/> units of a bag stack into an empty bag slot or onto a matching stack
    /// (ADR-020); completes after the next tick.
    /// </summary>
    ValueTask<WorldMutationOutcome> SubmitSplitInventorySlotAsync(ulong connectionId, ushort fromSlot, ushort toSlot, ushort quantity, ushort expectedItemId);

    /// <summary>Queues merging and sorting the backpack (<c>9..35</c>) (ADR-020); completes after the next tick.</summary>
    ValueTask<WorldMutationOutcome> SubmitSortInventoryAsync(ulong connectionId);

    /// <summary>Queues destroying items in <paramref name="slotIndex"/> permanently (ADR-020); completes after the next tick.</summary>
    ValueTask<WorldMutationOutcome> SubmitDestroyInventoryItemAsync(ulong connectionId, ushort slotIndex, int quantity, ushort expectedItemId);

    /// <summary>
    /// Queues enhancing the weapon, tool or armor piece in <paramref name="slotIndex"/> (<c>+0..+10</c>); completes after
    /// the next tick (ADR-017).
    /// </summary>
    ValueTask<WorldMutationOutcome> SubmitUpgradeInventoryItemAsync(ulong connectionId, ushort slotIndex);

    /// <summary>
    /// Queues dropping items from <paramref name="slotIndex"/> onto the ground as loot; completes after the next tick
    /// (ADR-017).
    /// </summary>
    ValueTask<WorldMutationOutcome> SubmitDropInventoryItemAsync(ulong connectionId, ushort slotIndex, int quantity, ushort expectedItemId);

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
