// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Abstractions.World;
using Carbon.Application.World.Players;
using Carbon.Application.World.Replication;
using Carbon.Application.World.Commands;
using Carbon.Contracts.Common;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Crafting;

namespace Carbon.Application.World;

public sealed partial class AuthoritativeWorld
{
    /// <inheritdoc/>
    public bool GetInventorySnapshot(ulong connectionId, List<(ushort SlotIndex, ItemSlot Slot)> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.Clear();

        lock (_gate)
        {
            if (!_players.ByConnection.TryGetValue(connectionId, out PlayerState? state))
            {
                return false;
            }

            ReadOnlySpan<ItemSlot> slots = state.Inventory.Slots;
            for (int i = 0; i < slots.Length; i++)
            {
                if (!slots[i].IsEmpty)
                {
                    output.Add(((ushort)i, slots[i]));
                }
            }

            return true;
        }
    }

    /// <inheritdoc/>
    public ValueTask<WorldMutationOutcome> SubmitSelectHotbarSlotAsync(ulong connectionId, byte slotIndex) =>
        _commands.Submit(WorldCommandKind.SelectHotbarSlot, connectionId, slotIndex);

    /// <inheritdoc/>
    public ValueTask<WorldMutationOutcome> SubmitCraftRecipeAsync(ulong connectionId, ushort recipeId, ushort quantity) =>
        _commands.Submit(WorldCommandKind.CraftRecipe, connectionId, recipeId, quantity);

    /// <inheritdoc/>
    public ValueTask<WorldMutationOutcome> SubmitMoveInventorySlotAsync(ulong connectionId, ushort fromSlot, ushort toSlot) =>
        _commands.Submit(WorldCommandKind.MoveInventorySlot, connectionId, fromSlot, toSlot);

    /// <inheritdoc/>
    public ValueTask<WorldMutationOutcome> SubmitUpgradeInventoryItemAsync(ulong connectionId, ushort slotIndex) =>
        _commands.Submit(WorldCommandKind.UpgradeInventoryItem, connectionId, slotIndex);

    /// <inheritdoc/>
    public ValueTask<WorldMutationOutcome> SubmitDropInventoryItemAsync(ulong connectionId, ushort slotIndex, int quantity) =>
        _commands.Submit(WorldCommandKind.DropInventoryItem, connectionId, slotIndex, arg2: quantity);

    /// <summary>Updates the selected hotbar slot and harvest tier. Synchronous form used by the tick and tests; handlers use the matching <c>Submit*Async</c> (ADR-017).</summary>
    public OpStatus TrySelectHotbarSlot(ulong connectionId, byte slotIndex)
    {
        if (slotIndex >= PlayerInventoryLayout.HotbarCount)
        {
            return OpStatus.Invalid;
        }

        EquipmentLook look;
        ulong[]? recipients;
        PlayerStatsSnapshot statsSnap;

        lock (_gate)
        {
            if (!_players.ByConnection.TryGetValue(connectionId, out PlayerState? state))
            {
                return OpStatus.Forbidden;
            }

            byte tier = _inventory.ResolveHarvestTier(state.Inventory, slotIndex);
            state.Session = state.Session with { SelectedHotbarSlot = slotIndex, SelectedHarvestTier = tier };
            this.RecomputeDerivedStatsLocked(state);
            this.MarkCharacterDirtyLocked(state, inventoryChanged: false);
            statsSnap = this.BuildPlayerStatsSnapshotLocked(state, clearLevelUpPending: false);
            look = EquipmentLook.Of(state);
            recipients = this.CopyNearbyRecipientsLocked(state);
        }

        _outboundSink.SendPlayerStatsSync(connectionId, in statsSnap);
        if (recipients is not null)
        {
            look.SendTo(_outboundSink, recipients);
        }

        return OpStatus.Ok;
    }

    /// <summary>Validates and executes a crafting recipe on the player's general bag storage. Synchronous form used by the tick and tests; handlers use the matching <c>Submit*Async</c> (ADR-017).</summary>
    public OpStatus TryCraftRecipe(ulong connectionId, ushort recipeId, ushort quantity)
    {
        if (recipeId == 0 || quantity == 0 || quantity > 64)
        {
            return OpStatus.Invalid;
        }

        if (!_itemRules.Recipes.TryGet(recipeId, out CraftingRecipe? recipe))
        {
            return OpStatus.NotFound;
        }

        PlayerStatsSnapshot statsSnap;
        (ushort SlotIndex, ItemSlot Slot)[] modifiedCopy;
        bool walletChanged;
        long gold;
        long diamonds;
        ulong walletSequence;
        lock (_gate)
        {
            if (!_players.ByConnection.TryGetValue(connectionId, out PlayerState? state) ||
                !_simulation.Entities.HasTransform(state.Entity))
            {
                return OpStatus.Forbidden;
            }

            Vector2 playerPos = _simulation.Entities.GetTransform(state.Entity).Position;
            ulong sequenceBefore = state.Wallet.Sequence;
            OpStatus status = _inventory.Craft(state, recipe, quantity, playerPos, _mutationSlotScratch, out int crafted);
            if (status != OpStatus.Ok)
            {
                return status;
            }

            byte newTier = _inventory.ResolveHarvestTier(state.Inventory, state.Session.SelectedHotbarSlot);
            state.Session = state.Session with { SelectedHarvestTier = newTier };
            this.GrantExperienceLocked(state, (uint)(15 * crafted), out _);
            this.MarkCharacterDirtyLocked(state, inventoryChanged: true);
            statsSnap = this.BuildPlayerStatsSnapshotLocked(state, clearLevelUpPending: true);
            modifiedCopy = [.. _mutationSlotScratch];
            walletChanged = state.Wallet.Sequence != sequenceBefore;
            gold = state.Wallet.Gold;
            diamonds = state.Wallet.Diamonds;
            walletSequence = state.Wallet.Sequence;
        }

        this.SendSlotsOutbound(connectionId, modifiedCopy);
        _outboundSink.SendPlayerStatsSync(connectionId, in statsSnap);
        if (walletChanged)
        {
            _outboundSink.SendWalletSync(connectionId, gold, diamonds, walletSequence);
        }

        return OpStatus.Ok;
    }

    /// <summary>Moves, swaps, merges or equips an inventory slot, enforcing armor slot constraints. Synchronous form used by the tick and tests; handlers use the matching <c>Submit*Async</c> (ADR-017).</summary>
    public OpStatus TryMoveInventorySlot(ulong connectionId, ushort fromSlot, ushort toSlot)
    {
        EquipmentLook look = default;
        ulong[]? recipients = null;
        PlayerStatsSnapshot? statsSnap = null;
        (ushort SlotIndex, ItemSlot Slot)[] modifiedCopy;

        lock (_gate)
        {
            if (!_players.ByConnection.TryGetValue(connectionId, out PlayerState? state))
            {
                return OpStatus.Forbidden;
            }

            OpStatus status = _inventory.Move(state, fromSlot, toSlot, _mutationSlotScratch);
            if (status != OpStatus.Ok)
            {
                return status;
            }

            byte newTier = _inventory.ResolveHarvestTier(state.Inventory, state.Session.SelectedHotbarSlot);
            state.Session = state.Session with { SelectedHarvestTier = newTier };

            if (fromSlot >= PlayerInventoryLayout.BagLimit ||
                toSlot >= PlayerInventoryLayout.BagLimit ||
                fromSlot == state.Session.SelectedHotbarSlot ||
                toSlot == state.Session.SelectedHotbarSlot)
            {
                this.RecomputeDerivedStatsLocked(state);
                statsSnap = this.BuildPlayerStatsSnapshotLocked(state, clearLevelUpPending: false);
                look = EquipmentLook.Of(state);
                recipients = this.CopyNearbyRecipientsLocked(state);
            }

            this.MarkCharacterDirtyLocked(state, inventoryChanged: true);
            modifiedCopy = [.. _mutationSlotScratch];
        }

        this.SendSlotsOutbound(connectionId, modifiedCopy);
        if (statsSnap.HasValue)
        {
            PlayerStatsSnapshot s = statsSnap.Value;
            _outboundSink.SendPlayerStatsSync(connectionId, in s);
        }

        if (recipients is not null)
        {
            look.SendTo(_outboundSink, recipients);
        }

        return OpStatus.Ok;
    }

    /// <summary>Enhances the eligible weapon, tool or armor piece in the slot. Synchronous form used by the tick and tests; handlers use the matching <c>Submit*Async</c> (ADR-017).</summary>
    public OpStatus TryUpgradeInventoryItem(ulong connectionId, ushort slotIndex) =>
        this.TryUpgradeInventoryItem(connectionId, slotIndex, deterministicRollSeed: 0U);

    /// <summary>
    /// Attempts to enhance an eligible weapon, tool, or armor piece in <paramref name="slotIndex"/>,
    /// using <paramref name="deterministicRollSeed"/> when non-zero for deterministic test runs.
    /// </summary>
    public OpStatus TryUpgradeInventoryItem(ulong connectionId, ushort slotIndex, uint deterministicRollSeed)
    {
        PlayerStatsSnapshot statsSnap;
        bool succeeded;
        EquipmentLook look;
        ulong[]? recipients = null;
        (ushort SlotIndex, ItemSlot Slot)[] modifiedCopy;

        lock (_gate)
        {
            if (!_players.ByConnection.TryGetValue(connectionId, out PlayerState? state) ||
                !_simulation.Entities.HasTransform(state.Entity))
            {
                return OpStatus.Forbidden;
            }

            Vector2 playerPos = _simulation.Entities.GetTransform(state.Entity).Position;
            OpStatus status = _inventory.Upgrade(state, slotIndex, playerPos, deterministicRollSeed, _mutationSlotScratch, out succeeded, out uint experience);
            if (status != OpStatus.Ok)
            {
                return status;
            }

            if (experience > 0U)
            {
                this.GrantExperienceLocked(state, experience, out _);
            }

            this.RecomputeDerivedStatsLocked(state);
            statsSnap = this.BuildPlayerStatsSnapshotLocked(state, clearLevelUpPending: true);
            look = EquipmentLook.Of(state);
            if (succeeded && (slotIndex >= PlayerInventoryLayout.BagLimit || slotIndex == state.Session.SelectedHotbarSlot))
            {
                recipients = this.CopyNearbyRecipientsLocked(state);
            }

            this.MarkCharacterDirtyLocked(state, inventoryChanged: true);
            modifiedCopy = [.. _mutationSlotScratch];
        }

        this.SendSlotsOutbound(connectionId, modifiedCopy);
        _outboundSink.SendPlayerStatsSync(connectionId, in statsSnap);
        if (recipients is not null)
        {
            look.SendTo(_outboundSink, recipients);
        }

        return succeeded ? OpStatus.Ok : OpStatus.EnhancementFailed;
    }

    /// <summary>Drops items from the slot onto the ground as loot. Synchronous form used by the tick and tests; handlers use the matching <c>Submit*Async</c> (ADR-017).</summary>
    public OpStatus TryDropInventoryItem(ulong connectionId, ushort slotIndex, int quantity)
    {
        PlayerStatsSnapshot statsSnap;
        EquipmentLook look = default;
        ulong[]? recipients = null;
        (ushort SlotIndex, ItemSlot Slot)[] modifiedCopy;

        lock (_gate)
        {
            if (!_players.ByConnection.TryGetValue(connectionId, out PlayerState? state) ||
                !_simulation.Entities.HasTransform(state.Entity))
            {
                return OpStatus.Forbidden;
            }

            Vector2 playerPos = _simulation.Entities.GetTransform(state.Entity).Position;
            OpStatus status = _inventory.Drop(state, slotIndex, quantity, playerPos, _mutationSlotScratch);
            if (status != OpStatus.Ok)
            {
                return status;
            }

            this.RecomputeDerivedStatsLocked(state);
            statsSnap = this.BuildPlayerStatsSnapshotLocked(state, clearLevelUpPending: false);
            if (slotIndex >= PlayerInventoryLayout.BagLimit || slotIndex == state.Session.SelectedHotbarSlot)
            {
                look = EquipmentLook.Of(state);
                recipients = this.CopyNearbyRecipientsLocked(state);
            }

            this.MarkCharacterDirtyLocked(state, inventoryChanged: true);
            modifiedCopy = [.. _mutationSlotScratch];
        }

        this.SendSlotsOutbound(connectionId, modifiedCopy);
        _outboundSink.SendPlayerStatsSync(connectionId, in statsSnap);
        if (recipients is not null)
        {
            look.SendTo(_outboundSink, recipients);
        }

        return OpStatus.Ok;
    }

    /// <inheritdoc/>
    public OpStatus EnqueuePickupLoot(ulong connectionId, ulong lootNetworkId)
    {
        lock (_gate)
        {
            if (!_players.ByConnection.TryGetValue(connectionId, out PlayerState? state))
            {
                return OpStatus.Forbidden;
            }

            state.PendingPickupLootId = lootNetworkId;
            return OpStatus.Ok;
        }
    }

    private void SendSlotsOutbound(ulong connectionId, (ushort SlotIndex, ItemSlot Slot)[] slots)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            (ushort idx, ItemSlot slot) = slots[i];
            _outboundSink.SendInventorySlotChanged(
                connectionId,
                idx,
                slot.ItemId,
                slot.Quantity,
                slot.Durability,
                ItemEnhancementPolicy.GetUpgradeLevel(in slot),
                slot.FreshnessSeconds);
        }
    }

    private void SpawnGroundLootLocked(Vector2 worldPos, in ItemSlot item) => _groundLoot.Spawn(worldPos, in item);

    private void QueueLootVisualRefreshLocked(ulong lootNetId, Vector2 pos)
    {
        if (!_replication.TryBuildSpawnSnapshot(lootNetId, out EntitySpawnSnapshot updatedSnap))
        {
            return;
        }

        this.CollectNearbyConnectionsAroundWorldLocked(pos, _aoiRecipientsScratch);
        for (int i = 0; i < _aoiRecipientsScratch.Count; i++)
        {
            ulong recipient = _aoiRecipientsScratch[i];
            _outbound.Pending.Despawns.Add(new OutboundDespawn(recipient, lootNetId, _currentTick));
            _outbound.Pending.Spawns.Add(new OutboundSpawn(recipient, updatedSnap));
        }
    }

    /// <summary>Copies the connections near the player (for a reply sent after the lock), or <see langword="null"/> when none.</summary>
    private ulong[]? CopyNearbyRecipientsLocked(PlayerState state)
    {
        if (!_simulation.Entities.HasTransform(state.Entity))
        {
            return null;
        }

        this.CollectNearbyConnectionsAroundWorldLocked(_simulation.Entities.GetTransform(state.Entity).Position, _aoiRecipientsScratch);
        return _aoiRecipientsScratch.Count > 0 ? [.. _aoiRecipientsScratch] : null;
    }
}
