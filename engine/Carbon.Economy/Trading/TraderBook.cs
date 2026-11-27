// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Contracts.Common;
using Carbon.Economy.Currency;
using Carbon.Economy.Vendor;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Primitives.Random;

namespace Carbon.Economy.Trading;

/// <summary>Units sold by one trader in one restock epoch: the only trader state, persisted in <c>trader_stock</c>.</summary>
/// <param name="TraderKey">Trader key.</param>
/// <param name="Epoch">Restock epoch the counts belong to.</param>
/// <param name="Sold">Units sold per stock slot.</param>
public sealed record TraderSalesRecord(ulong TraderKey, long Epoch, int[] Sold);

/// <summary>
/// Server-side trader state and rules (ADR-025): the stock of each epoch is rolled from the secret trader stock key
/// (ADR-026), only units sold
/// are remembered, and a purchase checks slot, epoch, remaining stock, gold and bag space before it commits wallet,
/// inventory and the sold count together. Not thread-safe: call on the world tick (the world lock).
/// </summary>
public sealed class TraderBook
{
    private readonly GameplayKey _stockKey;
    private readonly Dictionary<ulong, TraderSalesRecord> _sales = new();
    private readonly HashSet<ulong> _dirty = new();

    /// <summary>Initializes a new instance of the <see cref="TraderBook"/> class.</summary>
    /// <param name="stockKey">Secret trader stock key (server-only, never the world seed).</param>
    public TraderBook(GameplayKey stockKey) => _stockKey = stockKey;

    /// <summary>Gets the number of traders with sales recorded.</summary>
    public int TrackedTraders => _sales.Count;

    /// <summary>
    /// Writes the trader's current offers for a buyer and returns how many were written.
    /// </summary>
    /// <param name="catalog">Trader profiles.</param>
    /// <param name="prices">Price rules.</param>
    /// <param name="trader">The trader.</param>
    /// <param name="unixSeconds">Server wall-clock time.</param>
    /// <param name="buyerLevel">Buyer level (consumable price bands).</param>
    /// <param name="destination">Receives the offers.</param>
    /// <param name="epoch">The current restock epoch.</param>
    /// <param name="secondsToRestock">Seconds until the next restock.</param>
    /// <returns>Number of offers, or -1 when the profile is unknown.</returns>
    public int GetOffers(
        TraderCatalog catalog,
        PriceBook prices,
        in TraderIdentity trader,
        long unixSeconds,
        int buyerLevel,
        Span<TraderOffer> destination,
        out long epoch,
        out int secondsToRestock)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(prices);
        epoch = 0;
        secondsToRestock = 0;
        if (!catalog.TryGet(trader.ProfileId, out TraderProfile profile))
        {
            return -1;
        }

        epoch = TraderClock.EpochAt(trader.Key, profile, unixSeconds);
        secondsToRestock = TraderClock.SecondsUntilRestock(trader.Key, profile, unixSeconds);
        Span<TraderStockSlot> slots = stackalloc TraderStockSlot[TraderCatalog.MaxSlots];
        int count = Math.Min(TraderStockRoller.Roll(profile, _stockKey, trader.Key, epoch, trader.Style, slots), destination.Length);
        int[]? sold = this.SoldIn(trader.Key, epoch);
        for (int i = 0; i < count; i++)
        {
            int remaining = Math.Max(0, slots[i].Quantity - (sold is not null && i < sold.Length ? sold[i] : 0));
            destination[i] = new TraderOffer(slots[i].ItemId, slots[i].Quantity, remaining, TraderStockRoller.UnitPrice(prices, slots[i], buyerLevel));
        }

        return count;
    }

    /// <summary>Buys units from a stock slot. On any failure nothing changes.</summary>
    /// <param name="catalog">Trader profiles.</param>
    /// <param name="prices">Price rules.</param>
    /// <param name="trader">The trader.</param>
    /// <param name="unixSeconds">Server wall-clock time.</param>
    /// <param name="wallet">Buyer wallet.</param>
    /// <param name="inventory">Buyer inventory.</param>
    /// <param name="slotIndex">Stock slot.</param>
    /// <param name="quantity">Units (positive).</param>
    /// <param name="expectedEpoch">The epoch the buyer's view was priced in.</param>
    /// <param name="buyerLevel">Buyer level.</param>
    /// <param name="slotLimit">Only the first <paramref name="slotLimit"/> inventory slots receive items (the bag).</param>
    /// <param name="tick">Simulation tick (ledger).</param>
    /// <returns>The result.</returns>
    public TradeResult TryBuy(
        TraderCatalog catalog,
        PriceBook prices,
        in TraderIdentity trader,
        long unixSeconds,
        Wallet wallet,
        InventoryContainer inventory,
        int slotIndex,
        int quantity,
        long expectedEpoch,
        int buyerLevel,
        int slotLimit,
        ulong tick = 0)
    {
        ArgumentNullException.ThrowIfNull(wallet);
        ArgumentNullException.ThrowIfNull(inventory);
        Span<TraderOffer> offers = stackalloc TraderOffer[TraderCatalog.MaxSlots];
        int count = this.GetOffers(catalog, prices, trader, unixSeconds, buyerLevel, offers, out long epoch, out _);
        if (count < 0)
        {
            return TradeResult.UnknownTrader;
        }

        if (epoch != expectedEpoch)
        {
            return TradeResult.StaleEpoch;
        }

        if ((uint)slotIndex >= (uint)count || quantity <= 0)
        {
            return TradeResult.InvalidRequest;
        }

        TraderOffer offer = offers[slotIndex];
        if (offer.Remaining < quantity)
        {
            return TradeResult.SoldOut;
        }

        long total;
        try
        {
            total = checked(offer.UnitPrice * quantity);
        }
        catch (OverflowException)
        {
            return TradeResult.InvalidRequest;
        }

        if (!wallet.CanAfford(CurrencyKind.Gold, total))
        {
            return TradeResult.InsufficientFunds;
        }

        if (!prices.Items.TryGet(offer.ItemId, out ItemCatalogEntry? item))
        {
            return TradeResult.InvalidRequest;
        }

        Span<ItemSlot> buffer = inventory.Capacity <= InventoryDraft.StackBufferLimit
            ? stackalloc ItemSlot[InventoryDraft.StackBufferLimit]
            : new ItemSlot[inventory.Capacity];
        InventoryDraft draft = InventoryDraft.Begin(inventory, buffer, slotLimit);
        if (draft.Add(new ItemSlot(offer.ItemId, quantity, Durability: item.MaxDurability), item.MaxStack) != 0)
        {
            return TradeResult.InventoryFull;
        }

        if (!wallet.TrySpend(CurrencyKind.Gold, total, CurrencyReason.VendorPurchase, tick).IsOk)
        {
            return TradeResult.InsufficientFunds;
        }

        draft.CommitTo(inventory);
        this.RecordSale(trader.Key, epoch, slotIndex, quantity, count);
        return TradeResult.Ok;
    }

    /// <summary>
    /// Buys a whole cart of stock lines (ADR-025 D18): the epoch must be current, every line must name a distinct stock
    /// slot that still offers the item the buyer saw with enough units left, the server's total must equal
    /// <paramref name="expectedGold"/>, the wallet must pay it and everything must fit in the bag. Then wallet, bag and
    /// sold counts change together; on any failure nothing changes.
    /// </summary>
    /// <param name="catalog">Trader profiles.</param>
    /// <param name="prices">Price rules.</param>
    /// <param name="trader">The trader.</param>
    /// <param name="unixSeconds">Server wall-clock time.</param>
    /// <param name="wallet">Buyer wallet.</param>
    /// <param name="inventory">Buyer inventory.</param>
    /// <param name="lines">Cart lines (stock slot, item, units).</param>
    /// <param name="expectedEpoch">The epoch the buyer's view was priced in.</param>
    /// <param name="expectedGold">The total the buyer confirmed.</param>
    /// <param name="buyerLevel">Buyer level.</param>
    /// <param name="slotLimit">Only the first <paramref name="slotLimit"/> inventory slots receive items (the bag).</param>
    /// <param name="tick">Simulation tick (ledger).</param>
    /// <returns>The outcome.</returns>
    public TradeBatchOutcome TryBuyBatch(
        TraderCatalog catalog,
        PriceBook prices,
        in TraderIdentity trader,
        long unixSeconds,
        Wallet wallet,
        InventoryContainer inventory,
        ReadOnlySpan<TradeLine> lines,
        long expectedEpoch,
        long expectedGold,
        int buyerLevel,
        int slotLimit,
        ulong tick = 0)
    {
        ArgumentNullException.ThrowIfNull(wallet);
        ArgumentNullException.ThrowIfNull(inventory);
        Span<TraderOffer> offers = stackalloc TraderOffer[TraderCatalog.MaxSlots];
        int count = this.GetOffers(catalog, prices, trader, unixSeconds, buyerLevel, offers, out long epoch, out _);
        if (count < 0 || lines.IsEmpty || lines.Length > count)
        {
            return TradeBatchOutcome.Fail(TradeBatchError.InvalidRequest);
        }

        if (epoch != expectedEpoch)
        {
            return TradeBatchOutcome.Fail(TradeBatchError.PricesChanged);
        }

        long total = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            TradeLine line = lines[i];
            if ((uint)line.Slot >= (uint)count || line.Quantity <= 0)
            {
                return TradeBatchOutcome.Fail(TradeBatchError.InvalidRequest, i);
            }

            for (int j = 0; j < i; j++)
            {
                if (lines[j].Slot == line.Slot)
                {
                    return TradeBatchOutcome.Fail(TradeBatchError.DuplicateLine, i);
                }
            }

            TraderOffer offer = offers[line.Slot];
            if (offer.ItemId != line.ItemId)
            {
                return TradeBatchOutcome.Fail(TradeBatchError.PricesChanged, i);
            }

            if (offer.Remaining < line.Quantity)
            {
                return TradeBatchOutcome.Fail(TradeBatchError.SoldOut, i);
            }

            try
            {
                total = checked(total + (offer.UnitPrice * line.Quantity));
            }
            catch (OverflowException)
            {
                return TradeBatchOutcome.Fail(TradeBatchError.InvalidRequest, i);
            }
        }

        if (total != expectedGold)
        {
            return TradeBatchOutcome.Fail(TradeBatchError.PricesChanged);
        }

        if (!wallet.CanAfford(CurrencyKind.Gold, total))
        {
            return TradeBatchOutcome.Fail(TradeBatchError.NotEnoughGold);
        }

        Span<ItemSlot> buffer = inventory.Capacity <= InventoryDraft.StackBufferLimit
            ? stackalloc ItemSlot[InventoryDraft.StackBufferLimit]
            : new ItemSlot[inventory.Capacity];
        InventoryDraft draft = InventoryDraft.Begin(inventory, buffer, slotLimit);
        for (int i = 0; i < lines.Length; i++)
        {
            TraderOffer offer = offers[lines[i].Slot];
            if (!prices.Items.TryGet(offer.ItemId, out ItemCatalogEntry? item))
            {
                return TradeBatchOutcome.Fail(TradeBatchError.InvalidRequest, i);
            }

            if (draft.Add(new ItemSlot(offer.ItemId, lines[i].Quantity, Durability: item.MaxDurability), item.MaxStack) != 0)
            {
                return TradeBatchOutcome.Fail(TradeBatchError.BagFull, i);
            }
        }

        if (!wallet.TrySpend(CurrencyKind.Gold, total, CurrencyReason.VendorPurchase, tick).IsOk)
        {
            return TradeBatchOutcome.Fail(TradeBatchError.NotEnoughGold);
        }

        draft.CommitTo(inventory);
        for (int i = 0; i < lines.Length; i++)
        {
            this.RecordSale(trader.Key, epoch, lines[i].Slot, lines[i].Quantity, count);
        }

        return TradeBatchOutcome.Ok(total);
    }

    /// <summary>Restores persisted sales (stale epochs are simply never matched).</summary>
    public void Import(TraderSalesRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        _sales[record.TraderKey] = record;
    }

    /// <summary>Moves the records changed since the last drain into <paramref name="destination"/>.</summary>
    public void DrainDirty(List<TraderSalesRecord> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        foreach (ulong key in _dirty)
        {
            if (_sales.TryGetValue(key, out TraderSalesRecord? record))
            {
                destination.Add(record with { Sold = (int[])record.Sold.Clone() });
            }
        }

        _dirty.Clear();
    }

    /// <summary>Marks records as dirty again (a persistence batch failed).</summary>
    public void MarkDirty(ulong traderKey) => _ = _dirty.Add(traderKey);

    private int[]? SoldIn(ulong traderKey, long epoch) =>
        _sales.TryGetValue(traderKey, out TraderSalesRecord? record) && record.Epoch == epoch ? record.Sold : null;

    private void RecordSale(ulong traderKey, long epoch, int slotIndex, int quantity, int slotCount)
    {
        int[]? sold = this.SoldIn(traderKey, epoch);
        if (sold is null || sold.Length < slotCount)
        {
            int[] fresh = new int[Math.Max(slotCount, TraderCatalog.MaxSlots)];
            sold?.CopyTo(fresh, 0);
            sold = fresh;
            _sales[traderKey] = new TraderSalesRecord(traderKey, epoch, sold);
        }

        sold[slotIndex] += quantity;
        _ = _dirty.Add(traderKey);
    }
}
