// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Carbon.Contracts.Common;
using Carbon.Contracts.Items.S2C;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;

namespace Carbon.Desktop.Abstractions.World;

/// <summary>
/// The trade window's staged trades (ADR-025 D18): a sell basket of bag lines and a buy cart of stock lines. Staging
/// only reserves units in the UI; nothing reaches the server until <see cref="PressSell"/> or <see cref="PressBuy"/>
/// sends the whole side as one all-or-nothing batch. Prices come from the server's last <see cref="TraderView"/>; when a
/// later one changes what a staged line costs, the next press re-confirms instead of sending (D21). Main thread only.
/// </summary>
public sealed class TradeBasket
{
    /// <summary>Most basket lines (one per bag slot).</summary>
    public const int MaxSellLines = PlayerInventoryLayout.BagLimit;

    /// <summary>Most cart lines (one per stock slot; the server's <c>TraderCatalog.MaxSlots</c>).</summary>
    public const int MaxBuyLines = 16;

    private readonly List<TradeBasketLine> _sell = new(MaxSellLines);
    private readonly List<TradeBasketLine> _buy = new(MaxBuyLines);
    private TraderView? _pricedBy;
    private bool _enhancedConfirmed;

    /// <summary>Gets the sell basket lines.</summary>
    public IReadOnlyList<TradeBasketLine> SellLines => _sell;

    /// <summary>Gets the buy cart lines.</summary>
    public IReadOnlyList<TradeBasketLine> BuyLines => _buy;

    /// <summary>Gets the gold the basket would fetch.</summary>
    public long SellTotal => Total(_sell);

    /// <summary>Gets the gold the cart would cost.</summary>
    public long BuyTotal => Total(_buy);

    /// <summary>Gets whether a basket price moved since the player last confirmed (the next Sell re-confirms).</summary>
    public bool SellPricesChanged { get; private set; }

    /// <summary>Gets whether the cart's prices or stock moved since the player last confirmed (the next Buy re-confirms).</summary>
    public bool BuyPricesChanged { get; private set; }

    /// <summary>Gets whether the basket holds an enhanced item whose sale the player has not confirmed yet.</summary>
    public bool SellNeedsEnhancedConfirm => !_enhancedConfirmed && HasEnhanced(_sell);

    /// <summary>Gets whether a committed basket is waiting for the server's answer.</summary>
    public bool IsSellPending { get; private set; }

    /// <summary>Gets whether a committed cart is waiting for the server's answer.</summary>
    public bool IsBuyPending { get; private set; }

    /// <summary>Gets whether nothing is staged.</summary>
    public bool IsEmpty => _sell.Count == 0 && _buy.Count == 0;

    /// <summary>Gets the units of <paramref name="bagSlot"/> set aside in the basket.</summary>
    public int ReservedAt(int bagSlot)
    {
        int index = IndexOf(_sell, bagSlot);
        return index >= 0 ? _sell[index].Quantity : 0;
    }

    /// <summary>Gets the units of stock slot <paramref name="stockSlot"/> in the cart.</summary>
    public int InCartAt(int stockSlot)
    {
        int index = IndexOf(_buy, stockSlot);
        return index >= 0 ? _buy[index].Quantity : 0;
    }

    /// <summary>
    /// Moves units of a bag item into the basket (a click or a drop): one unit, or every unit not yet set aside when
    /// <paramref name="wholeStack"/>. Equipment, empty slots and items the trader does not buy are refused.
    /// </summary>
    public bool AddFromBag(int bagSlot, in ItemSlot slot, TraderView trader, bool wholeStack)
    {
        ArgumentNullException.ThrowIfNull(trader);
        if (this.IsSellPending || (uint)bagSlot >= PlayerInventoryLayout.BagLimit || slot.IsEmpty)
        {
            return false;
        }

        int price = trader.SellPriceOf(slot.ItemId);
        int index = IndexOf(_sell, bagSlot);
        int staged = index >= 0 ? _sell[index].Quantity : 0;
        int free = slot.Quantity - staged;
        if (price <= 0 || free <= 0 || (index < 0 && _sell.Count >= MaxSellLines))
        {
            return false;
        }

        this.Remember(trader);
        TradeBasketLine line = new(bagSlot, slot.ItemId, staged + (wholeStack ? free : 1), price, slot.UpgradeLevel);
        if (index >= 0)
        {
            _sell[index] = line;
        }
        else
        {
            _sell.Add(line);
        }

        if (slot.UpgradeLevel > 0)
        {
            _enhancedConfirmed = false;
        }

        return true;
    }

    /// <summary>Changes a basket line by <paramref name="delta"/> units, within <c>0..</c> the slot's units (0 returns it).</summary>
    public bool AdjustSell(int lineIndex, int delta, in ItemSlot slot)
    {
        if (this.IsSellPending || (uint)lineIndex >= (uint)_sell.Count)
        {
            return false;
        }

        TradeBasketLine line = _sell[lineIndex];
        int quantity = Math.Min(line.Quantity + delta, slot.ItemId == line.ItemId ? slot.Quantity : 0);
        if (quantity <= 0)
        {
            _sell.RemoveAt(lineIndex);
        }
        else
        {
            _sell[lineIndex] = line with { Quantity = quantity };
        }

        return true;
    }

    /// <summary>Adds units of a stock slot to the cart, up to the units the trader has left.</summary>
    public bool AddFromStock(int stockSlot, TraderView trader, int units)
    {
        ArgumentNullException.ThrowIfNull(trader);
        if (this.IsBuyPending || units <= 0 || (uint)stockSlot >= (uint)trader.ItemIds.Length)
        {
            return false;
        }

        int index = IndexOf(_buy, stockSlot);
        int staged = index >= 0 ? _buy[index].Quantity : 0;
        int free = trader.Remaining[stockSlot] - staged;
        if (free <= 0 || (index < 0 && _buy.Count >= MaxBuyLines))
        {
            return false;
        }

        this.Remember(trader);
        TradeBasketLine line = new(stockSlot, trader.ItemIds[stockSlot], staged + Math.Min(units, free), trader.UnitPrices[stockSlot]);
        if (index >= 0)
        {
            _buy[index] = line;
        }
        else
        {
            _buy.Add(line);
        }

        return true;
    }

    /// <summary>Changes a cart line by <paramref name="delta"/> units, within <c>0..</c> the units left (0 removes it).</summary>
    public bool AdjustBuy(int lineIndex, int delta, TraderView trader)
    {
        ArgumentNullException.ThrowIfNull(trader);
        if (this.IsBuyPending || (uint)lineIndex >= (uint)_buy.Count)
        {
            return false;
        }

        TradeBasketLine line = _buy[lineIndex];
        int left = (uint)line.Slot < (uint)trader.Remaining.Length ? trader.Remaining[line.Slot] : 0;
        int quantity = Math.Min(line.Quantity + delta, left);
        if (quantity <= 0)
        {
            _buy.RemoveAt(lineIndex);
        }
        else
        {
            _buy[lineIndex] = line with { Quantity = quantity };
        }

        return true;
    }

    /// <summary>Returns every basket line (the basket's Clear); the bag never changed.</summary>
    public void ClearSell()
    {
        if (!this.IsSellPending)
        {
            _sell.Clear();
            _enhancedConfirmed = false;
            this.SellPricesChanged = false;
        }
    }

    /// <summary>Empties the cart (the cart's Clear).</summary>
    public void ClearBuy()
    {
        if (!this.IsBuyPending)
        {
            _buy.Clear();
            this.BuyPricesChanged = false;
        }
    }

    /// <summary>Returns every staged line (Clear, closing the window, walking away). The server never saw them.</summary>
    public void Clear()
    {
        _sell.Clear();
        _buy.Clear();
        _pricedBy = null;
        _enhancedConfirmed = false;
        this.SellPricesChanged = false;
        this.BuyPricesChanged = false;
        this.IsSellPending = false;
        this.IsBuyPending = false;
    }

    /// <summary>
    /// Re-prices the staged lines against the server's latest stock and drops what the bag no longer holds. Call once a
    /// frame. A new stock that changes a staged price, the restock epoch, or a cart line's item flags a re-confirm.
    /// </summary>
    public void Sync(TraderView trader, ReadOnlySpan<ItemSlot> bag)
    {
        ArgumentNullException.ThrowIfNull(trader);
        if (_pricedBy is not null && !ReferenceEquals(trader, _pricedBy))
        {
            this.Reprice(trader, _pricedBy.Epoch != trader.Epoch);
        }

        _pricedBy = trader;
        for (int i = _sell.Count - 1; i >= 0; i--)
        {
            TradeBasketLine line = _sell[i];
            ItemSlot slot = (uint)line.Slot < (uint)bag.Length ? bag[line.Slot] : ItemSlot.Empty;
            if (slot.IsEmpty || slot.ItemId != line.ItemId)
            {
                _sell.RemoveAt(i);
            }
            else if (slot.Quantity < line.Quantity)
            {
                _sell[i] = line with { Quantity = slot.Quantity };
            }
        }
    }

    /// <summary>
    /// Commits the basket: re-confirms changed prices or an enhanced item first, then sends every line and the total the
    /// player saw as one batch.
    /// </summary>
    public TradeBasketStep PressSell(ITraderSession session, ulong traderNetworkId)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_sell.Count == 0)
        {
            return TradeBasketStep.Nothing;
        }

        if (this.IsSellPending)
        {
            return TradeBasketStep.Busy;
        }

        if (this.SellPricesChanged)
        {
            this.SellPricesChanged = false;
            return TradeBasketStep.ConfirmPrices;
        }

        if (this.SellNeedsEnhancedConfirm)
        {
            _enhancedConfirmed = true;
            return TradeBasketStep.ConfirmEnhanced;
        }

        if (!session.TrySellBatch(traderNetworkId, CollectionsMarshal.AsSpan(_sell), this.SellTotal))
        {
            return TradeBasketStep.Nothing;
        }

        this.IsSellPending = true;
        return TradeBasketStep.Sent;
    }

    /// <summary>Commits the cart: re-confirms changed prices first, refuses a total above <paramref name="gold"/>, then sends it.</summary>
    public TradeBasketStep PressBuy(ITraderSession session, TraderView trader, long gold)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(trader);
        if (_buy.Count == 0)
        {
            return TradeBasketStep.Nothing;
        }

        if (this.IsBuyPending)
        {
            return TradeBasketStep.Busy;
        }

        if (this.BuyPricesChanged)
        {
            this.BuyPricesChanged = false;
            return TradeBasketStep.ConfirmPrices;
        }

        if (this.BuyTotal > gold)
        {
            return TradeBasketStep.NotEnoughGold;
        }

        if (!session.TryBuyBatch(trader.TraderNetworkId, trader.Epoch, CollectionsMarshal.AsSpan(_buy), this.BuyTotal))
        {
            return TradeBasketStep.Nothing;
        }

        this.IsBuyPending = true;
        return TradeBasketStep.Sent;
    }

    /// <summary>
    /// Applies the server's answer: a success empties that side (the bag and wallet syncs carry the result); a refusal
    /// keeps the lines for the player to fix, and changed prices need a re-confirm.
    /// </summary>
    public void Apply(in TradeBatchReply reply)
    {
        bool sell = reply.Kind == TraderBatchResult.KindSell;
        if (sell)
        {
            this.IsSellPending = false;
        }
        else
        {
            this.IsBuyPending = false;
        }

        // A success empties the side. The stock its own trade pushed may have arrived first and looked like a price
        // change, so that flag goes too.
        if (reply.IsOk)
        {
            (sell ? _sell : _buy).Clear();
            if (sell)
            {
                _enhancedConfirmed = false;
                this.SellPricesChanged = false;
            }
            else
            {
                this.BuyPricesChanged = false;
            }

            return;
        }

        if (reply.Error == TradeBatchError.PricesChanged)
        {
            if (sell)
            {
                this.SellPricesChanged = true;
            }
            else
            {
                this.BuyPricesChanged = true;
            }
        }
    }

    /// <summary>Gets the toast for a refused batch.</summary>
    public static string DescribeFailure(in TradeBatchReply reply) => reply.Error switch
    {
        TradeBatchError.BagChanged => GameText.Get("trade_basket.failed_bag_changed"),
        TradeBatchError.NotSellable => GameText.Get("trade_basket.failed_not_sellable"),
        TradeBatchError.PricesChanged => GameText.Get("trade_basket.failed_prices_changed"),
        TradeBatchError.SoldOut => GameText.Get("trade_basket.failed_sold_out"),
        TradeBatchError.NotEnoughGold => GameText.Get("trade_basket.failed_not_enough_gold"),
        TradeBatchError.BagFull => GameText.Get("trade_basket.failed_bag_full"),
        TradeBatchError.WalletFull => GameText.Get("trade_basket.failed_wallet_full"),
        TradeBatchError.None or TradeBatchError.InvalidRequest or TradeBatchError.DuplicateLine => reply.Status == OpStatus.OutOfRange
            ? GameText.Get("trade_basket.failed_too_far")
            : GameText.Get("trade_basket.failed"),
        _ => GameText.Get("trade_basket.failed"),
    };

    private void Remember(TraderView trader) => _pricedBy ??= trader;

    private void Reprice(TraderView trader, bool restocked)
    {
        for (int i = _sell.Count - 1; i >= 0; i--)
        {
            TradeBasketLine line = _sell[i];
            int price = trader.SellPriceOf(line.ItemId);
            if (price != line.UnitPrice)
            {
                this.SellPricesChanged = true;
                if (price <= 0)
                {
                    _sell.RemoveAt(i);
                    continue;
                }

                _sell[i] = line with { UnitPrice = price };
            }
        }

        for (int i = _buy.Count - 1; i >= 0; i--)
        {
            TradeBasketLine line = _buy[i];
            bool gone = (uint)line.Slot >= (uint)trader.ItemIds.Length || trader.ItemIds[line.Slot] != line.ItemId;
            if (gone)
            {
                this.BuyPricesChanged = true;
                _buy.RemoveAt(i);
                continue;
            }

            int price = trader.UnitPrices[line.Slot];
            int quantity = Math.Min(line.Quantity, trader.Remaining[line.Slot]);
            if (restocked || price != line.UnitPrice || quantity != line.Quantity)
            {
                this.BuyPricesChanged = true;
            }

            if (quantity <= 0)
            {
                _buy.RemoveAt(i);
            }
            else
            {
                _buy[i] = line with { UnitPrice = price, Quantity = quantity };
            }
        }
    }

    private static long Total(List<TradeBasketLine> lines)
    {
        long total = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            total += lines[i].Subtotal;
        }

        return total;
    }

    private static bool HasEnhanced(List<TradeBasketLine> lines)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].UpgradeLevel > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static int IndexOf(List<TradeBasketLine> lines, int slot)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Slot == slot)
            {
                return i;
            }
        }

        return -1;
    }
}
