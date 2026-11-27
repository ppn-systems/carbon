// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.Application.World.Actors;
using Carbon.Application.World.Commands;
using Carbon.Application.World.Inventory;
using Carbon.Application.World.Players;
using Carbon.Application.World.Replication;
using Carbon.Contracts.Common;
using Carbon.Contracts.Items.S2C;
using Carbon.Economy;
using Carbon.Economy.Trading;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Simulation.ECS;
using Carbon.Simulation.World;

namespace Carbon.Application.World.Commerce;

/// <summary>
/// Trading with village traders (ADR-025): requests are queued like every world command (ADR-017) and drained on the
/// tick under the world lock; stock comes from <see cref="TraderBook"/> (seeded per restock epoch, only sales are state);
/// buy and sell need a live trader NPC within <see cref="EconomyService.VendorRangeSq"/>. Successful requests stage the
/// changed slots, the wallet and a fresh trader stock into the outbound tick buffer. Sales persist through
/// <see cref="ITraderStockRepository"/>.
/// </summary>
internal sealed class TradeService : IWorldTradeService
{
    private readonly Lock _gate;
    private readonly WorldCommandQueue _commands = new();
    private readonly TraderBook _book;
    private readonly PlayerRegistry _players;
    private readonly WorldSimulation _simulation;
    private readonly Dictionary<ulong, NpcActorMeta> _actors;
    private readonly Func<GameEconomy?> _economy;
    private readonly OutboundTickBuffer _outbound;
    private readonly TimeProvider _time;
    private readonly Func<ulong> _tick;
    private readonly Action<PlayerState> _markDirty;
    private readonly ITraderStockRepository _repository;
    private readonly List<(ushort SlotIndex, ItemSlot Slot)> _modified = new(8);

    /// <summary>Initializes a new instance of the <see cref="TradeService"/> class.</summary>
    public TradeService(
        Lock gate,
        ulong worldSeed,
        PlayerRegistry players,
        WorldSimulation simulation,
        Dictionary<ulong, NpcActorMeta> actors,
        Func<GameEconomy?> economy,
        OutboundTickBuffer outbound,
        TimeProvider time,
        Func<ulong> tick,
        Action<PlayerState> markDirty,
        ITraderStockRepository? repository)
    {
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _book = new TraderBook(worldSeed);
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
        _actors = actors ?? throw new ArgumentNullException(nameof(actors));
        _economy = economy ?? throw new ArgumentNullException(nameof(economy));
        _outbound = outbound ?? throw new ArgumentNullException(nameof(outbound));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _tick = tick ?? throw new ArgumentNullException(nameof(tick));
        _markDirty = markDirty ?? throw new ArgumentNullException(nameof(markDirty));
        _repository = repository ?? new InMemoryTraderStockRepository();
    }

    /// <summary>Profile id of the blacksmith, who also repairs gear (ADR-025 D17).</summary>
    public const string BlacksmithProfile = "blacksmith";

    /// <summary>Gets the trader state (tests).</summary>
    public TraderBook Book => _book;

    /// <summary>Maps a profile id to its wire kind.</summary>
    public static byte KindOf(string? profileId) => profileId switch
    {
        "merchant" => TraderStock.KindMerchant,
        "general" => TraderStock.KindGeneral,
        BlacksmithProfile => TraderStock.KindBlacksmith,
        _ => 0,
    };

    /// <inheritdoc/>
    public ValueTask<WorldMutationOutcome> SubmitTraderOpenAsync(ulong connectionId, ulong traderNetworkId) =>
        _commands.SubmitTargeted(WorldCommandKind.TraderOpen, connectionId, traderNetworkId, 0, 0, 0);

    /// <inheritdoc/>
    public ValueTask<WorldMutationOutcome> SubmitTraderBuyAsync(ulong connectionId, ulong traderNetworkId, byte slotIndex, ushort quantity, long epoch) =>
        epoch is < 0 or > int.MaxValue
            ? ValueTask.FromResult(new WorldMutationOutcome(OpStatus.Invalid, 0))
            : _commands.SubmitTargeted(WorldCommandKind.TraderBuy, connectionId, traderNetworkId, slotIndex, quantity, (int)epoch);

    /// <inheritdoc/>
    public ValueTask<WorldMutationOutcome> SubmitTraderSellAsync(ulong connectionId, ulong traderNetworkId, ushort slotIndex, ushort quantity) =>
        _commands.SubmitTargeted(WorldCommandKind.TraderSell, connectionId, traderNetworkId, slotIndex, quantity, 0);

    /// <summary>Runs the queued trade requests. Call on the tick, under the world lock.</summary>
    public void DrainLocked() => _commands.Drain(this.Execute);

    /// <summary>Opens a trader (synchronous form for the tick and tests). Call under the world lock.</summary>
    public OpStatus OpenLocked(ulong connectionId, ulong traderNetworkId) =>
        this.TryResolve(connectionId, traderNetworkId, out PlayerState? state, out TraderIdentity trader, out GameEconomy? economy) is OpStatus status && status != OpStatus.Ok
            ? status
            : this.QueueStock(state!, traderNetworkId, trader, economy!);

    /// <summary>Buys from a trader (synchronous form). Call under the world lock.</summary>
    public OpStatus BuyLocked(ulong connectionId, ulong traderNetworkId, int slotIndex, int quantity, long epoch)
    {
        OpStatus status = this.TryResolve(connectionId, traderNetworkId, out PlayerState? state, out TraderIdentity trader, out GameEconomy? economy);
        if (status != OpStatus.Ok)
        {
            return status;
        }

        if (quantity is <= 0 or > EconomyService.MaxVendorQuantity)
        {
            return OpStatus.Invalid;
        }

        InventoryContainer bag = state!.Inventory;
        Span<ItemSlot> before = stackalloc ItemSlot[bag.Slots.Length];
        bag.Slots.CopyTo(before);
        TradeResult result = _book.TryBuy(
            economy!.Traders!,
            economy.Prices,
            trader,
            _time.GetUtcNow().ToUnixTimeSeconds(),
            state.Wallet,
            bag,
            slotIndex,
            quantity,
            epoch,
            state.Level,
            Math.Min(PlayerInventoryLayout.BagLimit, bag.Capacity),
            _tick());
        if (result != TradeResult.Ok)
        {
            return result.ToOpStatus();
        }

        this.StageChanges(state, before);
        return this.QueueStock(state, traderNetworkId, trader, economy);
    }

    /// <summary>Sells to a trader at the economy's vendor sell price (synchronous form). Call under the world lock.</summary>
    public OpStatus SellLocked(ulong connectionId, ulong traderNetworkId, int slotIndex, int quantity)
    {
        OpStatus status = this.TryResolve(connectionId, traderNetworkId, out PlayerState? state, out TraderIdentity trader, out GameEconomy? economy);
        if (status != OpStatus.Ok)
        {
            return status;
        }

        if (quantity <= 0 || (uint)slotIndex >= PlayerInventoryLayout.BagLimit)
        {
            return OpStatus.Invalid;
        }

        InventoryContainer bag = state!.Inventory;
        Span<ItemSlot> before = stackalloc ItemSlot[bag.Slots.Length];
        bag.Slots.CopyTo(before);
        OpStatus sold = economy!.Vendor.TrySellSlot(state.Wallet, bag, slotIndex, quantity, _tick()).Status;
        if (sold != OpStatus.Ok)
        {
            return sold;
        }

        this.StageChanges(state, before);
        return this.QueueStock(state, traderNetworkId, trader, economy);
    }

    /// <summary>Loads persisted sales. Call once at boot.</summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        IReadOnlyList<PersistedTraderSales> rows = await _repository.LoadAllAsync(ct).ConfigureAwait(false);
        lock (_gate)
        {
            foreach (PersistedTraderSales row in rows)
            {
                int[] sold = new int[row.Sold.Length / 4];
                for (int i = 0; i < sold.Length; i++)
                {
                    sold[i] = BinaryPrimitives.ReadInt32LittleEndian(row.Sold.AsSpan(i * 4));
                }

                _book.Import(new TraderSalesRecord(unchecked((ulong)row.TraderKey), row.Epoch, sold));
            }
        }
    }

    /// <summary>Saves the sales changed since the last flush; on failure they are retried next time.</summary>
    public async Task FlushAsync(CancellationToken ct = default)
    {
        List<TraderSalesRecord> dirty = [];
        lock (_gate)
        {
            _book.DrainDirty(dirty);
        }

        if (dirty.Count == 0)
        {
            return;
        }

        List<PersistedTraderSales> rows = new(dirty.Count);
        foreach (TraderSalesRecord record in dirty)
        {
            byte[] blob = new byte[record.Sold.Length * 4];
            for (int i = 0; i < record.Sold.Length; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(i * 4), record.Sold[i]);
            }

            rows.Add(new PersistedTraderSales(unchecked((long)record.TraderKey), record.Epoch, blob));
        }

        try
        {
            await _repository.SaveBatchAsync(rows, ct).ConfigureAwait(false);
        }
        catch
        {
            lock (_gate)
            {
                foreach (TraderSalesRecord record in dirty)
                {
                    _book.MarkDirty(record.TraderKey);
                }
            }

            throw;
        }
    }

    private WorldMutationOutcome Execute(WorldCommand command)
    {
        ulong trader = (uint)command.TileX | ((ulong)(uint)command.TileY << 32);
        OpStatus status = command.Kind switch
        {
            WorldCommandKind.TraderOpen => this.OpenLocked(command.ConnectionId, trader),
            WorldCommandKind.TraderBuy => this.BuyLocked(command.ConnectionId, trader, command.Arg0, command.Arg1, command.Arg2),
            WorldCommandKind.TraderSell => this.SellLocked(command.ConnectionId, trader, command.Arg0, command.Arg1),
            WorldCommandKind.HarvestProp or WorldCommandKind.PlaceStructure or WorldCommandKind.DemolishStructure or
            WorldCommandKind.ToggleDoor or WorldCommandKind.SetResting or WorldCommandKind.SelectHotbarSlot or
            WorldCommandKind.CraftRecipe or WorldCommandKind.MoveInventorySlot or WorldCommandKind.UpgradeInventoryItem or
            WorldCommandKind.DropInventoryItem or WorldCommandKind.SplitInventorySlot or WorldCommandKind.SortInventory or
            WorldCommandKind.DestroyInventoryItem or WorldCommandKind.VendorBuy or WorldCommandKind.VendorSell or
            WorldCommandKind.RepairItem or WorldCommandKind.AllocateCharacterPoint => OpStatus.Invalid,
            _ => OpStatus.Invalid,
        };
        return new WorldMutationOutcome(status, 0);
    }

    private OpStatus TryResolve(ulong connectionId, ulong traderNetworkId, out PlayerState? state, out TraderIdentity trader, out GameEconomy? economy)
    {
        trader = default;
        economy = _economy();
        if (!_players.ByConnection.TryGetValue(connectionId, out state) || !_simulation.Entities.HasTransform(state.Entity))
        {
            return OpStatus.Forbidden;
        }

        if (economy?.Traders is null)
        {
            return OpStatus.Forbidden;
        }

        if (!_actors.TryGetValue(traderNetworkId, out NpcActorMeta meta) || meta.TraderProfileId is null ||
            !_simulation.TryGetEntityByNetworkId(traderNetworkId, out Entity npc) || !_simulation.Entities.HasTransform(npc))
        {
            return OpStatus.NotFound;
        }

        Vector2 playerPos = _simulation.Entities.GetTransform(state.Entity).Position;
        Vector2 npcPos = _simulation.Entities.GetTransform(npc).Position;
        if (Vector2.DistanceSquared(playerPos, npcPos) > EconomyService.VendorRangeSq)
        {
            return OpStatus.OutOfRange;
        }

        trader = new TraderIdentity(meta.TraderKey, meta.TraderProfileId, meta.TraderStyle);
        return OpStatus.Ok;
    }

    private void StageChanges(PlayerState state, ReadOnlySpan<ItemSlot> before)
    {
        _modified.Clear();
        InventoryService.CollectModifiedSlots(before, state.Inventory.Slots, _modified);
        for (int i = 0; i < _modified.Count; i++)
        {
            _outbound.Pending.SlotChanges.Add(new OutboundSlotChange(state.Session.ConnectionId, _modified[i].SlotIndex, _modified[i].Slot));
        }

        _outbound.Pending.WalletSyncs.Add(new OutboundWalletSync(state.Session.ConnectionId, state.Wallet.Gold, state.Wallet.Diamonds, state.Wallet.Sequence));
        _markDirty(state);
    }

    private OpStatus QueueStock(PlayerState state, ulong traderNetworkId, in TraderIdentity trader, GameEconomy economy)
    {
        Span<TraderOffer> offers = stackalloc TraderOffer[TraderCatalog.MaxSlots];
        int count = _book.GetOffers(
            economy.Traders!,
            economy.Prices,
            trader,
            _time.GetUtcNow().ToUnixTimeSeconds(),
            state.Level,
            offers,
            out long epoch,
            out int secondsToRestock);
        if (count < 0)
        {
            return OpStatus.NotFound;
        }

        ushort[] itemIds = new ushort[count];
        int[] prices = new int[count];
        ushort[] stocked = new ushort[count];
        ushort[] remaining = new ushort[count];
        for (int i = 0; i < count; i++)
        {
            itemIds[i] = (ushort)offers[i].ItemId;
            prices[i] = (int)Math.Min(int.MaxValue, offers[i].UnitPrice);
            stocked[i] = (ushort)Math.Min(ushort.MaxValue, offers[i].Stocked);
            remaining[i] = (ushort)Math.Min(ushort.MaxValue, offers[i].Remaining);
        }

        // What the trader pays for each distinct item in the player's bag (the vendor sell price, a 25 % spread).
        List<ushort> sellIds = [];
        List<int> sellPrices = [];
        ReadOnlySpan<ItemSlot> slots = state.Inventory.Slots;
        for (int i = 0; i < Math.Min(PlayerInventoryLayout.BagLimit, slots.Length); i++)
        {
            ItemSlot slot = slots[i];
            long price = slot.IsEmpty ? 0 : economy.Prices.GetSellPrice(slot.ItemId);
            if (price > 0 && !sellIds.Contains((ushort)slot.ItemId))
            {
                sellIds.Add((ushort)slot.ItemId);
                sellPrices.Add((int)Math.Min(int.MaxValue, price));
            }
        }

        // A blacksmith also quotes the repair of every worn item in the bag (the forge repair price, PriceBook.GetRepairCost).
        bool canRepair = trader.ProfileId == BlacksmithProfile;
        List<ushort> repairSlots = [];
        List<int> repairCosts = [];
        for (int i = 0; canRepair && i < Math.Min(PlayerInventoryLayout.BagLimit, slots.Length); i++)
        {
            long cost = economy.Prices.GetRepairCost(slots[i]);
            if (cost > 0)
            {
                repairSlots.Add((ushort)i);
                repairCosts.Add((int)Math.Min(int.MaxValue, cost));
            }
        }

        _outbound.Pending.TraderStocks.Add(new OutboundTraderStock(
            state.Session.ConnectionId,
            new TraderStockSnapshot(
                traderNetworkId,
                KindOf(trader.ProfileId),
                epoch,
                secondsToRestock,
                itemIds,
                prices,
                stocked,
                remaining,
                [.. sellIds],
                [.. sellPrices],
                canRepair,
                [.. repairSlots],
                [.. repairCosts])));
        return OpStatus.Ok;
    }
}
