// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Abstractions.World;
using Carbon.Application.World.Commands;
using Carbon.Application.World.Loot;
using Carbon.Contracts.Common;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Crafting;
using Carbon.Items.Definitions;
using Carbon.Maps.Building;

using Carbon.Simulation.ECS;

namespace Carbon.Application.World;

public sealed partial class AuthoritativeWorld
{
    private const int CraftingStationReachTiles = 5;

    /// <inheritdoc/>
    public bool GetInventorySnapshot(ulong connectionId, List<(ushort SlotIndex, ItemSlot Slot)> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.Clear();

        lock (_gate)
        {
            if (!_playersByConnection.TryGetValue(connectionId, out ActivePlayerState? state))
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

        ushort eqNetHead, eqNetChest, eqNetLegs, eqNetFeet, eqNetWeapon, eqLevel;
        byte eqWeaponUpg;
        float eqMaxHp;
        ulong netId;
        ulong[]? recipientsCopy = null;
        PlayerStatsSnapshot statsSnap;

        lock (_gate)
        {
            if (!_playersByConnection.TryGetValue(connectionId, out ActivePlayerState? state))
            {
                return OpStatus.Forbidden;
            }

            byte tier = this.ResolveSlotHarvestTier(state.Inventory, slotIndex);
            state.Session = state.Session with { SelectedHotbarSlot = slotIndex, SelectedHarvestTier = tier };
            this.RecomputeDerivedStatsLocked(state);
            this.MarkCharacterDirtyLocked(state, inventoryChanged: false);
            statsSnap = this.BuildPlayerStatsSnapshotLocked(state, clearLevelUpPending: false);

            eqNetHead = (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.HeadSlot).ItemId;
            eqNetChest = (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.ChestSlot).ItemId;
            eqNetLegs = (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.LegsSlot).ItemId;
            eqNetFeet = (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.FeetSlot).ItemId;
            ItemSlot activeWeapon = slotIndex < state.Inventory.Capacity ? state.Inventory.GetSlot(slotIndex) : ItemSlot.Empty;
            eqNetWeapon = (ushort)activeWeapon.ItemId;
            eqWeaponUpg = ItemEnhancementPolicy.GetUpgradeLevel(in activeWeapon);
            eqLevel = state.Level;
            eqMaxHp = state.Stats.MaxHealth;
            netId = state.Session.PlayerNetworkId;

            if (_simulation.Entities.HasTransform(state.Entity))
            {
                Vector2 pPos = _simulation.Entities.GetTransform(state.Entity).Position;
                this.CollectNearbyConnectionsAroundWorldLocked(pPos, _aoiRecipientsScratch);
                if (_aoiRecipientsScratch.Count > 0)
                {
                    recipientsCopy = [.. _aoiRecipientsScratch];
                }
            }
        }

        _outboundSink.SendPlayerStatsSync(connectionId, in statsSnap);

        if (recipientsCopy is not null)
        {
            _outboundSink.SendEntityEquipmentChanged(
                recipientsCopy,
                netId,
                eqNetHead,
                eqNetChest,
                eqNetLegs,
                eqNetFeet,
                eqNetWeapon,
                eqWeaponUpg,
                eqLevel,
                eqMaxHp);
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
            if (!_playersByConnection.TryGetValue(connectionId, out ActivePlayerState? state) ||
                !_simulation.Entities.HasTransform(state.Entity))
            {
                return OpStatus.Forbidden;
            }

            // recipes.json rules: the station must be nearby; character level gates both unlock and skill (no crafting
            // skills are tracked yet, so the character level stands in for the recipe's skill level).
            Vector2 playerPos = _simulation.Entities.GetTransform(state.Entity).Position;
            if (!this.IsCraftingStationAvailableLocked(recipe.Station, playerPos))
            {
                return OpStatus.OutOfRange;
            }

            CraftContext context = new(recipe.Station, state.Level, state.Level);
            int bagLimit = Math.Min(PlayerInventoryLayout.BagLimit, state.Inventory.Capacity);
            _mutationSlotScratch.Clear();
            ReadOnlySpan<ItemSlot> beforeSlots = state.Inventory.Slots;
            Span<ItemSlot> beforeSnapshot = stackalloc ItemSlot[beforeSlots.Length];
            beforeSlots.CopyTo(beforeSnapshot);
            ulong sequenceBefore = state.Wallet.Sequence;

            int crafted = 0;
            CraftResult firstFailure = CraftResult.Ok;
            for (int q = 0; q < quantity; q++)
            {
                CraftResult result = _economy is not null
                    ? _economy.Checkout.TryCraft(state.Wallet, state.Inventory, recipe, context, _currentTick, bagLimit)
                    : RecipeMatcher.TryCraft(state.Inventory, recipe, _itemRules.ItemCatalog, context, bagLimit);
                if (!result.IsOk)
                {
                    firstFailure = result;
                    break;
                }

                crafted++;
            }

            if (crafted == 0)
            {
                return firstFailure.Status;
            }

            CollectModifiedSlots(beforeSnapshot, state.Inventory.Slots, _mutationSlotScratch);
            byte newTier = this.ResolveSlotHarvestTier(state.Inventory, state.Session.SelectedHotbarSlot);
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
        bool statsChanged = false;
        ushort headId = 0, chestId = 0, legsId = 0, feetId = 0, weaponId = 0, level = 1;
        byte weaponUpg = 0;
        float maxHp = 0f;
        ulong netId = 0UL;
        ulong[]? recipientsCopy = null;
        PlayerStatsSnapshot? statsSnap = null;
        (ushort SlotIndex, ItemSlot Slot)[] modifiedCopy;

        lock (_gate)
        {
            if (!_playersByConnection.TryGetValue(connectionId, out ActivePlayerState? state))
            {
                return OpStatus.Forbidden;
            }

            if (fromSlot >= state.Inventory.Capacity || toSlot >= state.Inventory.Capacity)
            {
                return OpStatus.Invalid;
            }

            _mutationSlotScratch.Clear();
            ReadOnlySpan<ItemSlot> before = state.Inventory.Slots;
            Span<ItemSlot> beforeSnap = stackalloc ItemSlot[before.Length];
            before.CopyTo(beforeSnap);

            if (!PlayerInventoryLayout.TryMoveOrEquipSlot(_itemRules, state.Inventory, fromSlot, toSlot))
            {
                return OpStatus.Invalid;
            }

            CollectModifiedSlots(beforeSnap, state.Inventory.Slots, _mutationSlotScratch);
            byte newTier = this.ResolveSlotHarvestTier(state.Inventory, state.Session.SelectedHotbarSlot);
            state.Session = state.Session with { SelectedHarvestTier = newTier };

            if (fromSlot >= PlayerInventoryLayout.BagLimit ||
                toSlot >= PlayerInventoryLayout.BagLimit ||
                fromSlot == state.Session.SelectedHotbarSlot ||
                toSlot == state.Session.SelectedHotbarSlot)
            {
                this.RecomputeDerivedStatsLocked(state);
                statsChanged = true;
                statsSnap = this.BuildPlayerStatsSnapshotLocked(state, clearLevelUpPending: false);
                headId = (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.HeadSlot).ItemId;
                chestId = (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.ChestSlot).ItemId;
                legsId = (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.LegsSlot).ItemId;
                feetId = (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.FeetSlot).ItemId;
                ItemSlot held = state.Inventory.GetSlot(state.Session.SelectedHotbarSlot);
                weaponId = (ushort)held.ItemId;
                weaponUpg = ItemEnhancementPolicy.GetUpgradeLevel(in held);
                level = state.Level;
                maxHp = state.Stats.MaxHealth;
                netId = state.Session.PlayerNetworkId;

                if (_simulation.Entities.HasTransform(state.Entity))
                {
                    this.CollectNearbyConnectionsAroundWorldLocked(_simulation.Entities.GetTransform(state.Entity).Position, _aoiRecipientsScratch);
                    if (_aoiRecipientsScratch.Count > 0)
                    {
                        recipientsCopy = [.. _aoiRecipientsScratch];
                    }
                }
            }

            this.MarkCharacterDirtyLocked(state, inventoryChanged: true);
            modifiedCopy = [.. _mutationSlotScratch];
        }

        this.SendSlotsOutbound(connectionId, modifiedCopy);

        if (statsChanged && statsSnap.HasValue)
        {
            PlayerStatsSnapshot s = statsSnap.Value;
            _outboundSink.SendPlayerStatsSync(connectionId, in s);
        }

        if (recipientsCopy is not null)
        {
            _outboundSink.SendEntityEquipmentChanged(
                recipientsCopy,
                netId,
                headId,
                chestId,
                legsId,
                feetId,
                weaponId,
                weaponUpg,
                level,
                maxHp);
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
        ushort headId = 0, chestId = 0, legsId = 0, feetId = 0, weaponId = 0, level = 1;
        byte weaponUpg = 0;
        float maxHp = 0f;
        ulong netId = 0UL;
        ulong[]? recipientsCopy = null;
        (ushort SlotIndex, ItemSlot Slot)[] modifiedCopy;

        lock (_gate)
        {
            if (!_playersByConnection.TryGetValue(connectionId, out ActivePlayerState? state) ||
                !_simulation.Entities.HasTransform(state.Entity))
            {
                return OpStatus.Forbidden;
            }

            Vector2 playerPos = _simulation.Entities.GetTransform(state.Entity).Position;
            if (!this.IsNearForgeStationLocked(playerPos))
            {
                return OpStatus.OutOfRange;
            }

            if (slotIndex >= state.Inventory.Capacity)
            {
                return OpStatus.Invalid;
            }

            ItemSlot target = state.Inventory.GetSlot(slotIndex);
            if (!ItemEnhancementPolicy.CanUpgrade(_itemRules, in target, out uint mat1, out int c1, out uint mat2, out int c2, out float successChance))
            {
                return OpStatus.Invalid;
            }

            if ((mat1 != 0U && state.Inventory.CountItem(mat1) < c1) ||
                (mat2 != 0U && state.Inventory.CountItem(mat2) < c2))
            {
                return OpStatus.Conflict;
            }

            _mutationSlotScratch.Clear();
            ReadOnlySpan<ItemSlot> before = state.Inventory.Slots;
            Span<ItemSlot> beforeSnap = stackalloc ItemSlot[before.Length];
            before.CopyTo(beforeSnap);

            if (mat1 != 0U)
            {
                _ = state.Inventory.TryRemove(mat1, c1);
            }

            if (mat2 != 0U)
            {
                _ = state.Inventory.TryRemove(mat2, c2);
            }

            double roll = deterministicRollSeed != 0U
                ? HashChunk(this.WorldSeed ^ deterministicRollSeed, slotIndex, (int)_currentTick) % 1000UL / 1000.0
                : Random.Shared.NextDouble();

            succeeded = roll <= successChance;
            if (succeeded && _itemRules.Items.TryGet(target.ItemId, out ItemArchetype? def))
            {
                byte nextLevel = (byte)(ItemEnhancementPolicy.GetUpgradeLevel(in target) + 1);
                float nextDur = ItemEnhancementPolicy.GetEffectiveMaxDurability(def, nextLevel);
                ItemSlot enhanced = ItemEnhancementPolicy.WithUpgradeLevel(in target, nextLevel) with { Durability = nextDur };
                state.Inventory.SetSlot(slotIndex, in enhanced);
                this.GrantExperienceLocked(state, (uint)(20 + (nextLevel * 5)), out _);
            }

            CollectModifiedSlots(beforeSnap, state.Inventory.Slots, _mutationSlotScratch);
            this.RecomputeDerivedStatsLocked(state);
            statsSnap = this.BuildPlayerStatsSnapshotLocked(state, clearLevelUpPending: true);

            headId = (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.HeadSlot).ItemId;
            chestId = (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.ChestSlot).ItemId;
            legsId = (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.LegsSlot).ItemId;
            feetId = (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.FeetSlot).ItemId;
            ItemSlot held = state.Inventory.GetSlot(state.Session.SelectedHotbarSlot);
            weaponId = (ushort)held.ItemId;
            weaponUpg = ItemEnhancementPolicy.GetUpgradeLevel(in held);
            level = state.Level;
            maxHp = state.Stats.MaxHealth;
            netId = state.Session.PlayerNetworkId;

            if (succeeded &&
                _simulation.Entities.HasTransform(state.Entity) &&
                (slotIndex >= PlayerInventoryLayout.BagLimit || slotIndex == state.Session.SelectedHotbarSlot))
            {
                this.CollectNearbyConnectionsAroundWorldLocked(_simulation.Entities.GetTransform(state.Entity).Position, _aoiRecipientsScratch);
                if (_aoiRecipientsScratch.Count > 0)
                {
                    recipientsCopy = [.. _aoiRecipientsScratch];
                }
            }

            this.MarkCharacterDirtyLocked(state, inventoryChanged: true);
            modifiedCopy = [.. _mutationSlotScratch];
        }

        this.SendSlotsOutbound(connectionId, modifiedCopy);
        _outboundSink.SendPlayerStatsSync(connectionId, in statsSnap);

        if (recipientsCopy is not null)
        {
            _outboundSink.SendEntityEquipmentChanged(
                recipientsCopy,
                netId,
                headId,
                chestId,
                legsId,
                feetId,
                weaponId,
                weaponUpg,
                level,
                maxHp);
        }

        return succeeded ? OpStatus.Ok : OpStatus.EnhancementFailed;
    }

    /// <summary>Drops items from the slot onto the ground as loot. Synchronous form used by the tick and tests; handlers use the matching <c>Submit*Async</c> (ADR-017).</summary>
    public OpStatus TryDropInventoryItem(ulong connectionId, ushort slotIndex, int quantity)
    {
        PlayerStatsSnapshot statsSnap;
        ulong[]? recipientsCopy = null;
        (ushort SlotIndex, ItemSlot Slot)[] modifiedCopy;
        ushort headId = 0, chestId = 0, legsId = 0, feetId = 0, weaponId = 0, level = 1;
        byte weaponUpg = 0;
        float maxHp = 0f;
        ulong netId = 0UL;

        lock (_gate)
        {
            if (!_playersByConnection.TryGetValue(connectionId, out ActivePlayerState? state) ||
                !_simulation.Entities.HasTransform(state.Entity))
            {
                return OpStatus.Forbidden;
            }

            if (slotIndex >= state.Inventory.Capacity)
            {
                return OpStatus.Invalid;
            }

            ItemSlot slot = state.Inventory.GetSlot(slotIndex);
            if (slot.IsEmpty)
            {
                return OpStatus.NotFound;
            }

            int dropQty = quantity <= 0 || quantity >= slot.Quantity ? slot.Quantity : quantity;
            ItemSlot dropStack = slot with { Quantity = dropQty };
            ItemSlot remainder = slot.Quantity > dropQty ? slot with { Quantity = slot.Quantity - dropQty } : ItemSlot.Empty;
            state.Inventory.SetSlot(slotIndex, in remainder);

            _mutationSlotScratch.Clear();
            _mutationSlotScratch.Add((slotIndex, remainder));

            Vector2 playerPos = _simulation.Entities.GetTransform(state.Entity).Position;
            this.SpawnGroundLootLocked(playerPos + new Vector2(28f, 6f), in dropStack);

            this.RecomputeDerivedStatsLocked(state);
            statsSnap = this.BuildPlayerStatsSnapshotLocked(state, clearLevelUpPending: false);
            if (slotIndex >= PlayerInventoryLayout.BagLimit || slotIndex == state.Session.SelectedHotbarSlot)
            {
                headId = (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.HeadSlot).ItemId;
                chestId = (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.ChestSlot).ItemId;
                legsId = (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.LegsSlot).ItemId;
                feetId = (ushort)state.Inventory.GetSlot(PlayerInventoryLayout.FeetSlot).ItemId;
                ItemSlot held = state.Inventory.GetSlot(state.Session.SelectedHotbarSlot);
                weaponId = (ushort)held.ItemId;
                weaponUpg = ItemEnhancementPolicy.GetUpgradeLevel(in held);
                level = state.Level;
                maxHp = state.Stats.MaxHealth;
                netId = state.Session.PlayerNetworkId;
                this.CollectNearbyConnectionsAroundWorldLocked(playerPos, _aoiRecipientsScratch);
                if (_aoiRecipientsScratch.Count > 0)
                {
                    recipientsCopy = [.. _aoiRecipientsScratch];
                }
            }

            this.MarkCharacterDirtyLocked(state, inventoryChanged: true);
            modifiedCopy = [.. _mutationSlotScratch];
        }

        this.SendSlotsOutbound(connectionId, modifiedCopy);
        _outboundSink.SendPlayerStatsSync(connectionId, in statsSnap);

        if (recipientsCopy is not null)
        {
            _outboundSink.SendEntityEquipmentChanged(
                recipientsCopy,
                netId,
                headId,
                chestId,
                legsId,
                feetId,
                weaponId,
                weaponUpg,
                level,
                maxHp);
        }

        return OpStatus.Ok;
    }

    /// <inheritdoc/>
    public OpStatus EnqueuePickupLoot(ulong connectionId, ulong lootNetworkId)
    {
        lock (_gate)
        {
            if (!_playersByConnection.TryGetValue(connectionId, out ActivePlayerState? state))
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
        if (!this.TryBuildSpawnSnapshotLocked(lootNetId, out EntitySpawnSnapshot updatedSnap))
        {
            return;
        }

        this.CollectNearbyConnectionsAroundWorldLocked(pos, _aoiRecipientsScratch);
        for (int i = 0; i < _aoiRecipientsScratch.Count; i++)
        {
            ulong recipient = _aoiRecipientsScratch[i];
            _pendingDespawns.Add(new OutboundDespawn(recipient, lootNetId, _currentTick));
            _pendingSpawns.Add(new OutboundSpawn(recipient, updatedSnap));
        }
    }

    private void CollectNearbyGroundLootLocked(ActivePlayerState observer, Vector2 obsPos)
    {
        if (_groundLoot.Count == 0)
        {
            return;
        }

        _staleLootScratch.Clear();
        _groundLoot.CollectAutoPickupCandidates(obsPos, _staleLootScratch);
        for (int i = 0; i < _staleLootScratch.Count; i++)
        {
            _ = this.TryPickupSpecificLootLocked(observer, _staleLootScratch[i], 36f * 36f);
        }
    }

    private bool TryPickupSpecificLootLocked(ActivePlayerState state, ulong lootNetId, float maxRangeSq)
    {
        if (!_groundLoot.TryGet(lootNetId, out GroundLootEntry meta) ||
            !_simulation.TryGetEntityByNetworkId(lootNetId, out Entity lootEnt) ||
            !_simulation.Entities.IsAlive(lootEnt) ||
            !_simulation.Entities.HasTransform(lootEnt) ||
            !_simulation.Entities.HasTransform(state.Entity))
        {
            return false;
        }

        Vector2 playerPos = _simulation.Entities.GetTransform(state.Entity).Position;
        Vector2 lootPos = _simulation.Entities.GetTransform(lootEnt).Position;
        if (Vector2.DistanceSquared(playerPos, lootPos) > maxRangeSq)
        {
            return false;
        }

        ItemSlot metaItem = meta.Item;
        int maxStack = _itemRules.GetMaxStackSize(metaItem.ItemId);
        ReadOnlySpan<ItemSlot> before = state.Inventory.Slots;
        Span<ItemSlot> snapshot = stackalloc ItemSlot[before.Length];
        before.CopyTo(snapshot);

        int remainder = PlayerInventoryLayout.TryAddBagItem(state.Inventory, in metaItem, maxStack);
        if (remainder >= metaItem.Quantity)
        {
            return false;
        }

        ReadOnlySpan<ItemSlot> after = state.Inventory.Slots;
        for (int i = 0; i < after.Length; i++)
        {
            if (snapshot[i] != after[i])
            {
                _pendingSlotChanges.Add(new OutboundSlotChange(state.Session.ConnectionId, (ushort)i, after[i]));
            }
        }

        this.MarkCharacterDirtyLocked(state, inventoryChanged: true);
        _groundLoot.ApplyPickup(lootNetId, lootEnt, lootPos, in meta, remainder);
        return true;
    }

    private byte ResolveSlotHarvestTier(InventoryContainer inventory, byte slotIndex)
    {
        if (slotIndex < inventory.Capacity)
        {
            ItemSlot slot = inventory.GetSlot(slotIndex);
            if (!slot.IsEmpty && _itemRules.Items.TryGet(slot.ItemId, out ItemArchetype? def))
            {
                return def.HarvestTier;
            }
        }

        return 0;
    }

    /// <summary>
    /// Checks whether <paramref name="station"/> can be used at <paramref name="worldPos"/>: hand recipes anywhere; forge at
    /// an anvil or forge-artisan NPC; workbench at a built workbench; stove at a built stove or campfire; alchemy at a
    /// shrine keeper.
    /// </summary>
    private bool IsCraftingStationAvailableLocked(CraftingStation station, Vector2 worldPos) => station switch
    {
        CraftingStation.None => true,
        CraftingStation.Forge => this.IsNearForgeStationLocked(worldPos),
        CraftingStation.Workbench => this.IsNearStructureLocked(worldPos, StructureType.Workbench, StructureType.Workbench),
        CraftingStation.Stove => this.IsNearStructureLocked(worldPos, StructureType.Stove, StructureType.Campfire),
        CraftingStation.Alchemy => this.HasNearbyNpcWithCapabilityLocked(worldPos, 175f * 175f, NpcCapabilities.Shrine),
        _ => false
    };

    private bool IsNearStructureLocked(Vector2 worldPos, StructureType first, StructureType second)
    {
        int ptx = (int)MathF.Floor(worldPos.X / this.TileSize);
        int pty = (int)MathF.Floor(worldPos.Y / this.TileSize);
        for (int dy = -CraftingStationReachTiles; dy <= CraftingStationReachTiles; dy++)
        {
            for (int dx = -CraftingStationReachTiles; dx <= CraftingStationReachTiles; dx++)
            {
                if (_chunkWorld.Buildings.TryGetStructure(ptx + dx, pty + dy, out StructureTile st) &&
                    (st.Type == first || st.Type == second) &&
                    !st.IsDestroyed)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsNearForgeStationLocked(Vector2 worldPos)
    {
        if (this.HasNearbyNpcWithCapabilityLocked(worldPos, 175f * 175f, NpcCapabilities.ForgeArtisan))
        {
            return true;
        }

        int ptx = (int)MathF.Floor(worldPos.X / this.TileSize);
        int pty = (int)MathF.Floor(worldPos.Y / this.TileSize);
        for (int dy = -5; dy <= 5; dy++)
        {
            for (int dx = -5; dx <= 5; dx++)
            {
                if (_chunkWorld.Buildings.TryGetStructure(ptx + dx, pty + dy, out StructureTile st) &&
                    st.Type == StructureType.ForgeAnvil &&
                    !st.IsDestroyed)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void RemoveItemTrackingChanges(
        InventoryContainer inventory,
        uint itemId,
        int quantity,
        List<(ushort SlotIndex, ItemSlot Slot)> changes)
    {
        ReadOnlySpan<ItemSlot> before = inventory.Slots;
        Span<ItemSlot> snapshot = stackalloc ItemSlot[before.Length];
        before.CopyTo(snapshot);

        _ = inventory.TryRemove(itemId, quantity);
        CollectModifiedSlots(snapshot, inventory.Slots, changes);
    }

    private static void CollectModifiedSlots(
        ReadOnlySpan<ItemSlot> before,
        ReadOnlySpan<ItemSlot> after,
        List<(ushort SlotIndex, ItemSlot Slot)> output)
    {
        for (int i = 0; i < after.Length; i++)
        {
            if (before[i] != after[i])
            {
                output.Add(((ushort)i, after[i]));
            }
        }
    }
}
