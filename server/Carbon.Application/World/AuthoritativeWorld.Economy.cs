// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using System.Threading.Tasks;
using Carbon.Abstractions.World;
using Carbon.Application.World.Players;
using Carbon.Application.World.Replication;
using Carbon.Application.World.Commands;
using Carbon.Contracts.Common;
using Carbon.Economy;
using Carbon.Economy.Currency;
using Carbon.Economy.Loot;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Primitives.Random;

namespace Carbon.Application.World;

/// <summary>
/// Economy integration: config-driven monster loot tables and wallet currency credited on kills.
/// </summary>
public sealed partial class AuthoritativeWorld
{
    private const int MaxLootStacksPerKill = 16;
    private const ushort MaxVendorQuantity = 999;
    private const float VendorRangeSq = 175f * 175f;

    /// <summary>
    /// Gets the gold and diamond balances of the player on <paramref name="connectionId"/>.
    /// </summary>
    /// <param name="connectionId">Player connection.</param>
    /// <param name="gold">Gold balance.</param>
    /// <param name="diamonds">Diamond balance.</param>
    /// <returns><see langword="true"/> when the player is in the world.</returns>
    public bool TryGetWalletBalances(ulong connectionId, out long gold, out long diamonds)
    {
        lock (_gate)
        {
            if (_players.ByConnection.TryGetValue(connectionId, out PlayerState? state))
            {
                gold = state.Wallet.Gold;
                diamonds = state.Wallet.Diamonds;
                return true;
            }
        }

        gold = 0;
        diamonds = 0;
        return false;
    }

    /// <summary>
    /// Maps a <see cref="WorldActorCatalog"/> monster archetype to its <c>loot.json</c> table id.
    /// </summary>
    /// <param name="archetypeId">Monster archetype id.</param>
    /// <returns>The loot table id, or <see langword="null"/> for actors without one.</returns>
    public static string? ResolveLootTableId(ushort archetypeId) => archetypeId switch
    {
        WorldActorCatalog.WildSlime => "slime",
        WorldActorCatalog.SkeletonArcher => "skeleton",
        WorldActorCatalog.OrcShaman => "orc",
        WorldActorCatalog.CrimsonDemon => "demon",
        WorldActorCatalog.RockGolem => "golem",
        WorldActorCatalog.FrostWolf => "wolf",
        WorldActorCatalog.AshWraith => "wraith",
        _ => null
    };

    /// <summary>
    /// Rolls the config loot table of a killed monster, spawns the item stacks as ground loot and credits gold and
    /// diamonds to the killer's wallet. Server-authoritative and deterministic per (world seed, tick, victim).
    /// </summary>
    /// <remarks><paramref name="tableId"/> is the monster definition's <c>lootTableId</c> (falls back to <see cref="ResolveLootTableId"/>).</remarks>
    /// <returns><see langword="false"/> when no economy or table applies (caller falls back to legacy drops).</returns>
    private bool TryRollEconomyLootLocked(PlayerState killer, string? tableId, ushort monsterLevel, ulong victimNetworkId, Vector2 position)
    {
        if (_economy is null
            || tableId is null
            || !_economy.Loot.TryGet(tableId, out LootTable? table))
        {
            return false;
        }

        LootContext context = new(monsterLevel, killer.Level, IsElite: table.IsElite);
        DeterministicRandom random = LootRoller.CreateRandom(this.WorldSeed, _currentTick, victimNetworkId);
        LootPityState pity = killer.LootPity;
        Span<LootDrop> drops = stackalloc LootDrop[MaxLootStacksPerKill];
        LootRollResult result = LootRoller.Roll(table, context, ref random, ref pity, drops);
        killer.LootPity = pity;

        for (int i = 0; i < result.ItemCount; i++)
        {
            LootDrop drop = drops[i];
            float durability = _economy.Items.TryGet(drop.ItemId, out ItemCatalogEntry? item) ? item.MaxDurability : 0f;
            ItemSlot stack = new(drop.ItemId, drop.Quantity, Durability: durability);
            Vector2 offset = new(((i % 4) - 1.5f) * 10f, i / 4 * 8f);
            this.SpawnGroundLootLocked(position + offset, in stack);
        }

        // Rejections (wallet at cap) leave the balance unchanged; the drop is simply forfeited.
        ulong sequenceBefore = killer.Wallet.Sequence;
        _ = killer.Wallet.TryCredit(CurrencyKind.Gold, result.Gold, CurrencyReason.MonsterLoot, _currentTick);
        _ = killer.Wallet.TryCredit(CurrencyKind.Diamond, result.Diamonds, CurrencyReason.MonsterLoot, _currentTick);
        if (killer.Wallet.Sequence != sequenceBefore)
        {
            this.EnqueueWalletSyncLocked(killer);
        }

        return true;
    }

    /// <inheritdoc/>
    public ValueTask<WorldMutationOutcome> SubmitVendorBuyAsync(ulong connectionId, ushort itemId, ushort quantity) =>
        _commands.Submit(WorldCommandKind.VendorBuy, connectionId, itemId, quantity);

    /// <inheritdoc/>
    public ValueTask<WorldMutationOutcome> SubmitVendorSellAsync(ulong connectionId, ushort slotIndex, ushort quantity) =>
        _commands.Submit(WorldCommandKind.VendorSell, connectionId, slotIndex, quantity);

    /// <inheritdoc/>
    public ValueTask<WorldMutationOutcome> SubmitRepairItemAsync(ulong connectionId, ushort slotIndex) =>
        _commands.Submit(WorldCommandKind.RepairItem, connectionId, slotIndex);

    /// <summary>Buys vendor-sold items for gold near a friendly town NPC. Synchronous form used by the tick and tests; handlers use the matching <c>Submit*Async</c> (ADR-017).</summary>
    public OpStatus TryVendorBuy(ulong connectionId, ushort itemId, ushort quantity)
    {
        if (quantity is 0 or > MaxVendorQuantity)
        {
            return OpStatus.Invalid;
        }

        return this.RunVendorOperationLocked(
            connectionId,
            requireForge: false,
            (economy, state, tick) => economy.Vendor.TryBuy(
                state.Wallet, state.Inventory, itemId, quantity, tick, Math.Min(PlayerInventoryLayout.BagLimit, state.Inventory.Capacity), state.Level).Status);
    }

    /// <summary>Sells units from a bag slot to a nearby friendly town NPC for gold. Synchronous form used by the tick and tests; handlers use the matching <c>Submit*Async</c> (ADR-017).</summary>
    public OpStatus TryVendorSell(ulong connectionId, ushort slotIndex, ushort quantity)
    {
        if (quantity == 0 || slotIndex >= PlayerInventoryLayout.BagLimit)
        {
            return OpStatus.Invalid;
        }

        return this.RunVendorOperationLocked(
            connectionId,
            requireForge: false,
            (economy, state, tick) => economy.Vendor.TrySellSlot(state.Wallet, state.Inventory, slotIndex, quantity, tick).Status);
    }

    /// <summary>Repairs the item in the slot to full durability for gold at a forge station. Synchronous form used by the tick and tests; handlers use the matching <c>Submit*Async</c> (ADR-017).</summary>
    public OpStatus TryRepairItem(ulong connectionId, ushort slotIndex) =>
        this.RunVendorOperationLocked(
            connectionId,
            requireForge: true,
            (economy, state, tick) => economy.Vendor.TryRepair(state.Wallet, state.Inventory, slotIndex, tick).Status);

    /// <summary>
    /// Runs a vendor/repair mutation under the world lock after the proximity check, then pushes the changed
    /// inventory slots and the new wallet balance to the owning client.
    /// </summary>
    private OpStatus RunVendorOperationLocked(
        ulong connectionId,
        bool requireForge,
        Func<GameEconomy, PlayerState, ulong, OpStatus> operation)
    {
        if (_economy is null)
        {
            return OpStatus.Forbidden;
        }

        (ushort SlotIndex, ItemSlot Slot)[] modifiedCopy;
        long gold;
        long diamonds;
        ulong sequence;
        lock (_gate)
        {
            if (!_players.ByConnection.TryGetValue(connectionId, out PlayerState? state) ||
                !_simulation.Entities.HasTransform(state.Entity))
            {
                return OpStatus.Forbidden;
            }

            Vector2 playerPos = _simulation.Entities.GetTransform(state.Entity).Position;
            bool inRange = requireForge
                ? this.IsNearForgeStationLocked(playerPos)
                : this.HasNearbyFriendlyNpcLocked(playerPos, VendorRangeSq);
            if (!inRange)
            {
                return OpStatus.OutOfRange;
            }

            ReadOnlySpan<ItemSlot> beforeSlots = state.Inventory.Slots;
            Span<ItemSlot> beforeSnapshot = stackalloc ItemSlot[beforeSlots.Length];
            beforeSlots.CopyTo(beforeSnapshot);

            OpStatus status = operation(_economy, state, _currentTick);
            if (status != OpStatus.Ok)
            {
                return status;
            }

            _mutationSlotScratch.Clear();
            CollectModifiedSlots(beforeSnapshot, state.Inventory.Slots, _mutationSlotScratch);
            this.MarkCharacterDirtyLocked(state, inventoryChanged: true);
            modifiedCopy = [.. _mutationSlotScratch];
            gold = state.Wallet.Gold;
            diamonds = state.Wallet.Diamonds;
            sequence = state.Wallet.Sequence;
        }

        this.SendSlotsOutbound(connectionId, modifiedCopy);
        _outboundSink.SendWalletSync(connectionId, gold, diamonds, sequence);
        return OpStatus.Ok;
    }

    private void EnqueueWalletSyncLocked(PlayerState state) =>
        _outbound.Pending.WalletSyncs.Add(new OutboundWalletSync(state.Session.ConnectionId, state.Wallet.Gold, state.Wallet.Diamonds, state.Wallet.Sequence));
}
