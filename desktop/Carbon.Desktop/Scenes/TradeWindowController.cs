// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.Hud;
using Carbon.Items.Containers;

namespace Carbon.Desktop.Scenes;

/// <summary>
/// What the mouse does in the trader window (ADR-025 D18), apart from hit-testing: a click on stock adds a unit to the cart
/// (Shift: 5); a click on a bag item moves a unit to the basket (Shift: the whole stack); dragging a bag item onto the
/// "To sell" card moves the whole stack; −/+ adjust a line (Shift: by 10); Clear returns a side; only Buy and Sell send
/// anything. Kept apart from <see cref="TradeInteraction"/> so it can be driven without a window.
/// </summary>
/// <param name="basket">The staged basket and cart.</param>
internal sealed class TradeWindowController(TradeBasket basket)
{
    /// <summary>UI: Shift+click on a stock row adds this many units.</summary>
    public const int BulkBuy = 5;

    /// <summary>UI: Shift+click on −/+ changes a line by this many units.</summary>
    public const int BulkStep = 10;

    /// <summary>UI: the cursor must travel this far (pixels) with the button held before a press becomes a drag.</summary>
    private const float DragThreshold = 6f;

    private readonly TradeBasket _basket = basket ?? throw new ArgumentNullException(nameof(basket));
    private int _pressedSlot = -1;
    private Vector2 _pressedAt;
    private bool _dragging;
    private bool _wasDown;

    /// <summary>Gets the basket and cart.</summary>
    public TradeBasket Basket => _basket;

    /// <summary>Gets the bag slot being dragged, or -1.</summary>
    public int DraggedSlot => _dragging ? _pressedSlot : -1;

    /// <summary>
    /// Once a frame: applies the server's answers to sent batches, then either re-prices the staged lines against the
    /// open trader's latest stock and the bag, or, when no trader is open (closed, walked away, replaced by another
    /// window), returns everything staged. Staged units never left the bag, so returning them sends nothing.
    /// </summary>
    public void Sync(TraderView? active, ReadOnlySpan<ItemSlot> bag, ITraderSession trading)
    {
        ArgumentNullException.ThrowIfNull(trading);
        while (trading.TryTakeBatchReply(out TradeBatchReply reply))
        {
            _basket.Apply(reply);
        }

        if (active is null)
        {
            _basket.Clear();
            _pressedSlot = -1;
            _dragging = false;
            _wasDown = false;
            return;
        }

        _basket.Sync(active, bag);
    }

    /// <summary>
    /// Handles one frame of mouse input over the window. Returns a toast to show, or <see langword="null"/>.
    /// </summary>
    /// <param name="hit">What the cursor is over this frame.</param>
    /// <param name="pressed">The left button went down this frame.</param>
    /// <param name="down">The left button is held.</param>
    /// <param name="shift">Shift is held.</param>
    /// <param name="mouse">Cursor position.</param>
    /// <param name="bag">The local bag (server state; the basket never changes it).</param>
    /// <param name="trader">The open trader's stock.</param>
    /// <param name="gold">The wallet.</param>
    /// <param name="session">Sends a committed basket or cart.</param>
    public string? Handle(TradeWindowHit hit, bool pressed, bool down, bool shift, Vector2 mouse, ReadOnlySpan<ItemSlot> bag, TraderView trader, long gold, ITraderSession session)
    {
        ArgumentNullException.ThrowIfNull(trader);
        ArgumentNullException.ThrowIfNull(session);
        bool released = _wasDown && !down;
        _wasDown = down;
        if (pressed)
        {
            return this.OnPress(hit, shift, mouse, trader, gold, session, bag);
        }

        if (_pressedSlot < 0)
        {
            return null;
        }

        if (down)
        {
            _dragging |= Vector2.DistanceSquared(mouse, _pressedAt) > DragThreshold * DragThreshold;
            return null;
        }

        if (released)
        {
            int slot = _pressedSlot;
            bool dragged = _dragging;
            _pressedSlot = -1;
            _dragging = false;
            if ((uint)slot < (uint)bag.Length && (dragged ? hit.IsSellCard : hit.Target == TradeWindowTarget.Bag && hit.Index == slot))
            {
                // A drop moves the whole stack; a click one unit, or the whole stack with Shift.
                _ = _basket.AddFromBag(slot, bag[slot], trader, wholeStack: dragged || shift);
            }
        }

        return null;
    }

    private string? OnPress(TradeWindowHit hit, bool shift, Vector2 mouse, TraderView trader, long gold, ITraderSession session, ReadOnlySpan<ItemSlot> bag)
    {
        int step = shift ? BulkStep : 1;
        switch (hit.Target)
        {
            case TradeWindowTarget.Bag:
                // Nothing happens on the press: the release decides between a click and a drag.
                _pressedSlot = hit.Index;
                _pressedAt = mouse;
                _dragging = false;
                return null;
            case TradeWindowTarget.Stock:
                _ = _basket.AddFromStock(hit.Index, trader, shift ? BulkBuy : 1);
                return null;
            case TradeWindowTarget.SellMinus or TradeWindowTarget.SellPlus when hit.Index < _basket.SellLines.Count:
                int bagSlot = _basket.SellLines[hit.Index].Slot;
                _ = _basket.AdjustSell(hit.Index, hit.Target == TradeWindowTarget.SellPlus ? step : -step, (uint)bagSlot < (uint)bag.Length ? bag[bagSlot] : ItemSlot.Empty);
                return null;
            case TradeWindowTarget.BuyMinus or TradeWindowTarget.BuyPlus:
                _ = _basket.AdjustBuy(hit.Index, hit.Target == TradeWindowTarget.BuyPlus ? step : -step, trader);
                return null;
            case TradeWindowTarget.ClearSell:
                _basket.ClearSell();
                return null;
            case TradeWindowTarget.ClearBuy:
                _basket.ClearBuy();
                return null;
            case TradeWindowTarget.SellButton:
                return Describe(_basket.PressSell(session, trader.TraderNetworkId));
            case TradeWindowTarget.BuyButton:
                return Describe(_basket.PressBuy(session, trader, gold));
            case TradeWindowTarget.None or TradeWindowTarget.SellArea or TradeWindowTarget.BuyArea or TradeWindowTarget.SellMinus or
                TradeWindowTarget.SellPlus or TradeWindowTarget.Repair or TradeWindowTarget.Enhance:
                return null;
            default:
                return null;
        }
    }

    private static string? Describe(TradeBasketStep step) => step switch
    {
        TradeBasketStep.NotEnoughGold => GameText.Get("trade_basket.not_enough_gold"),
        TradeBasketStep.Nothing or TradeBasketStep.Sent or TradeBasketStep.ConfirmPrices or TradeBasketStep.ConfirmEnhanced or
        TradeBasketStep.Busy => null,
        _ => null,
    };
}
