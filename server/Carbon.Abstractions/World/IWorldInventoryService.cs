// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
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
    /// the next tick (ADR-017), spending the protection scroll <paramref name="protectionItemId"/> when non-zero. On
    /// <see cref="Carbon.Contracts.Common.OpStatus.Ok"/> the revision is a packed <see cref="ForgeEnhancementReceipt"/> (ADR-024).
    /// </summary>
    ValueTask<WorldMutationOutcome> SubmitUpgradeInventoryItemAsync(ulong connectionId, ushort slotIndex, ushort protectionItemId = 0);

    /// <summary>
    /// Queues dropping items from <paramref name="slotIndex"/> onto the ground as loot; completes after the next tick
    /// (ADR-017).
    /// </summary>
    ValueTask<WorldMutationOutcome> SubmitDropInventoryItemAsync(ulong connectionId, ushort slotIndex, int quantity, ushort expectedItemId);

    /// <summary>
    /// Queues repairing ONE item (ADR-028): <paramref name="steps"/> steps of the item in <paramref name="slotIndex"/>
    /// at <paramref name="venue"/>, for the materials and fee in <c>repair.json</c>. Completes after the next tick
    /// (ADR-017) with a packed <see cref="RepairReceipt"/> in <see cref="WorldMutationOutcome.Revision"/>.
    /// </summary>
    ValueTask<WorldMutationOutcome> SubmitRepairItemAsync(ulong connectionId, ushort slotIndex, ushort expectedItemId, byte steps, RepairVenue venue);

    /// <summary>Gets trading with village traders (ADR-025).</summary>
    IWorldTradeService Trade { get; }
}
