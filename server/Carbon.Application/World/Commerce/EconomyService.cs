// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Application.World.Inventory;
using Carbon.Application.World.Loot;
using Carbon.Application.World.Players;
using Carbon.Application.World.Replication;
using Carbon.Contracts.Common;
using Carbon.Economy;
using Carbon.Economy.Currency;
using Carbon.Economy.Loot;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Primitives.Random;

namespace Carbon.Application.World.Commerce;

/// <summary>A vendor-side mutation of a player's wallet and inventory.</summary>
internal enum VendorOperation : byte
{
    /// <summary>Buy <c>quantity</c> of item <c>itemId</c> for gold.</summary>
    Buy = 1,

    /// <summary>Sell <c>quantity</c> units from bag slot <c>slotIndex</c> for gold.</summary>
    Sell = 2,

    /// <summary>Repair the item in <c>slotIndex</c> to full durability for gold (at a forge).</summary>
    Repair = 3,
}

/// <summary>
/// The world's economy (audit SOLID-1, extracted from <see cref="AuthoritativeWorld"/>): config-driven monster loot rolled
/// on kills (item stacks dropped as ground loot, gold and diamonds credited to the killer's wallet), vendor buy/sell and
/// forge repair with their proximity rules, and wallet sync messages. Deterministic per (world seed, tick, victim).
/// Called only under the world lock.
/// </summary>
internal sealed class EconomyService
{
    /// <summary>Most item stacks one kill can drop.</summary>
    public const int MaxLootStacksPerKill = 16;

    /// <summary>Largest quantity one vendor purchase may ask for.</summary>
    public const ushort MaxVendorQuantity = 999;

    /// <summary>Squared reach to a friendly town NPC for trading.</summary>
    public const float VendorRangeSq = 175f * 175f;

    private readonly ulong _worldSeed;
    private readonly Func<GameEconomy?> _economy;
    private readonly Func<ulong> _currentTick;
    private readonly GroundLootService _groundLoot;
    private readonly InventoryService _inventory;
    private readonly OutboundTickBuffer _outbound;
    private readonly Func<Vector2, float, bool> _hasNearbyFriendlyNpc;

    /// <summary>Initializes a new instance of the <see cref="EconomyService"/> class.</summary>
    public EconomyService(
        ulong worldSeed,
        Func<GameEconomy?> economy,
        Func<ulong> currentTick,
        GroundLootService groundLoot,
        InventoryService inventory,
        OutboundTickBuffer outbound,
        Func<Vector2, float, bool> hasNearbyFriendlyNpc)
    {
        _worldSeed = worldSeed;
        _economy = economy ?? throw new ArgumentNullException(nameof(economy));
        _currentTick = currentTick ?? throw new ArgumentNullException(nameof(currentTick));
        _groundLoot = groundLoot ?? throw new ArgumentNullException(nameof(groundLoot));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _outbound = outbound ?? throw new ArgumentNullException(nameof(outbound));
        _hasNearbyFriendlyNpc = hasNearbyFriendlyNpc ?? throw new ArgumentNullException(nameof(hasNearbyFriendlyNpc));
    }

    /// <summary>Gets whether the world runs an economy (vendors, loot tables, currency).</summary>
    public bool IsEnabled => _economy() is not null;

    /// <summary>Maps a <see cref="WorldActorCatalog"/> monster archetype to its <c>loot.json</c> table id.</summary>
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
        _ => null,
    };

    /// <summary>
    /// Rolls the config loot table of a killed monster, spawns the item stacks as ground loot and credits gold and
    /// diamonds to the killer's wallet (syncing it when the balance changed).
    /// </summary>
    /// <remarks><paramref name="tableId"/> is the monster definition's <c>lootTableId</c> (falls back to <see cref="ResolveLootTableId"/>).</remarks>
    /// <returns><see langword="false"/> when no economy or table applies (caller falls back to legacy drops).</returns>
    public bool TryRollLoot(PlayerState killer, string? tableId, ushort monsterLevel, ulong victimNetworkId, Vector2 position)
    {
        ArgumentNullException.ThrowIfNull(killer);
        GameEconomy? economy = _economy();
        if (economy is null
            || tableId is null
            || !economy.Loot.TryGet(tableId, out LootTable? table))
        {
            return false;
        }

        ulong tick = _currentTick();
        LootContext context = new(monsterLevel, killer.Level, IsElite: table.IsElite);
        DeterministicRandom random = LootRoller.CreateRandom(_worldSeed, tick, victimNetworkId);
        LootPityState pity = killer.LootPity;
        Span<LootDrop> drops = stackalloc LootDrop[MaxLootStacksPerKill];
        LootRollResult result = LootRoller.Roll(table, context, ref random, ref pity, drops);
        killer.LootPity = pity;

        for (int i = 0; i < result.ItemCount; i++)
        {
            LootDrop drop = drops[i];
            float durability = economy.Items.TryGet(drop.ItemId, out ItemCatalogEntry? item) ? item.MaxDurability : 0f;
            ItemSlot stack = new(drop.ItemId, drop.Quantity, Durability: durability);
            Vector2 offset = new(((i % 4) - 1.5f) * 10f, i / 4 * 8f);
            _groundLoot.Spawn(position + offset, in stack);
        }

        // Rejections (wallet at cap) leave the balance unchanged; the drop is simply forfeited.
        ulong sequenceBefore = killer.Wallet.Sequence;
        _ = killer.Wallet.TryCredit(CurrencyKind.Gold, result.Gold, CurrencyReason.MonsterLoot, tick);
        _ = killer.Wallet.TryCredit(CurrencyKind.Diamond, result.Diamonds, CurrencyReason.MonsterLoot, tick);
        if (killer.Wallet.Sequence != sequenceBefore)
        {
            this.QueueWalletSync(killer);
        }

        return true;
    }

    /// <summary>
    /// Runs a vendor operation at <paramref name="playerPos"/>: requires a friendly town NPC in reach (a forge for repair),
    /// then applies it through the economy's vendor and records the changed slots. <paramref name="arg"/> is the item id
    /// (buy) or slot index (sell, repair); <paramref name="quantity"/> is unused by repair.
    /// </summary>
    public OpStatus RunVendorOperation(
        PlayerState state,
        Vector2 playerPos,
        VendorOperation operation,
        ushort arg,
        ushort quantity,
        List<(ushort SlotIndex, ItemSlot Slot)> modified)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(modified);
        GameEconomy? economy = _economy();
        if (economy is null)
        {
            return OpStatus.Forbidden;
        }

        bool inRange = operation == VendorOperation.Repair
            ? _inventory.IsNearForgeStation(playerPos)
            : _hasNearbyFriendlyNpc(playerPos, VendorRangeSq);
        if (!inRange)
        {
            return OpStatus.OutOfRange;
        }

        ReadOnlySpan<ItemSlot> beforeSlots = state.Inventory.Slots;
        Span<ItemSlot> beforeSnapshot = stackalloc ItemSlot[beforeSlots.Length];
        beforeSlots.CopyTo(beforeSnapshot);

        ulong tick = _currentTick();
        OpStatus status = operation switch
        {
            VendorOperation.Buy => economy.Vendor.TryBuy(
                state.Wallet, state.Inventory, arg, quantity, tick, Math.Min(PlayerInventoryLayout.BagLimit, state.Inventory.Capacity), state.Level).Status,
            VendorOperation.Sell => economy.Vendor.TrySellSlot(state.Wallet, state.Inventory, arg, quantity, tick).Status,
            VendorOperation.Repair => economy.Vendor.TryRepair(state.Wallet, state.Inventory, arg, tick).Status,
            _ => OpStatus.Invalid,
        };
        if (status != OpStatus.Ok)
        {
            return status;
        }

        modified.Clear();
        InventoryService.CollectModifiedSlots(beforeSnapshot, state.Inventory.Slots, modified);
        return OpStatus.Ok;
    }

    /// <summary>Queues the player's wallet balances for its client.</summary>
    public void QueueWalletSync(PlayerState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _outbound.Pending.WalletSyncs.Add(new OutboundWalletSync(state.Session.ConnectionId, state.Wallet.Gold, state.Wallet.Diamonds, state.Wallet.Sequence));
    }
}
