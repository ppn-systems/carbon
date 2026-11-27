// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Contracts.Common;
using Carbon.Contracts.Items.S2C;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.Hud;
using Carbon.Desktop.Scenes;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>
/// The trade window stages trades and sends nothing until Sell or Buy is pressed (ADR-025 D18): a basket or cart only
/// reserves units in the UI, a commit sends exactly what is staged, changed prices need a re-confirm, and closing the
/// window returns everything.
/// </summary>
public sealed class TradeBasketTests
{
    private const ulong Trader = 77UL;
    private const uint Crystal = 101U;
    private const uint Pelt = 102U;
    private const uint Sword = 3U;

    [Fact]
    public void AddFromBag_StagesUnitsButSendsNothing()
    {
        RecordingTraderSession session = new();
        TradeBasket basket = new();
        ItemSlot[] bag = Bag();

        basket.AddFromBag(0, bag[0], View(), wholeStack: false).Should().BeTrue();
        basket.AddFromBag(0, bag[0], View(), wholeStack: false).Should().BeTrue();
        basket.AddFromBag(1, bag[1], View(), wholeStack: true).Should().BeTrue();

        session.Sent.Should().BeEmpty("staging never reaches the server, so its wallet and bag cannot change");
        basket.ReservedAt(0).Should().Be(2);
        basket.ReservedAt(1).Should().Be(20, "Shift moves the whole stack");
        basket.SellTotal.Should().Be((2 * 5) + (20 * 2));
        bag[0].Should().Be(new ItemSlot(Crystal, 10), "the local bag is the server's; reserving does not edit it");
    }

    [Fact]
    public void PressSell_SendsExactlyTheStagedLinesAndTotal_AndASuccessEmptiesTheBasket()
    {
        RecordingTraderSession session = new();
        TradeBasket basket = new();
        ItemSlot[] bag = Bag();
        basket.AddFromBag(0, bag[0], View(), wholeStack: false);
        basket.AdjustSell(0, 6, bag[0]).Should().BeTrue();
        basket.AddFromBag(1, bag[1], View(), wholeStack: true);

        basket.PressSell(session, Trader).Should().Be(TradeBasketStep.Sent);

        session.Sent.Should().ContainSingle();
        RecordingTraderSession.Batch sent = session.Sent[0];
        sent.Sell.Should().BeTrue();
        sent.Lines.Should().Equal(new TradeBasketLine(0, Crystal, 7, 5), new TradeBasketLine(1, Pelt, 20, 2));
        sent.ExpectedGold.Should().Be((7 * 5) + (20 * 2));
        basket.PressSell(session, Trader).Should().Be(TradeBasketStep.Busy, "one basket at a time");

        basket.Apply(new TradeBatchReply(Trader, TraderBatchResult.KindSell, OpStatus.Ok, TradeBatchError.None, TraderBatchResult.NoLine));
        basket.SellLines.Should().BeEmpty("a sold basket is done");
    }

    [Fact]
    public void Apply_RefusedBasket_KeepsEveryLine()
    {
        RecordingTraderSession session = new();
        TradeBasket basket = new();
        ItemSlot[] bag = Bag();
        basket.AddFromBag(1, bag[1], View(), wholeStack: true);
        basket.PressSell(session, Trader);

        basket.Apply(new TradeBatchReply(Trader, TraderBatchResult.KindSell, OpStatus.Conflict, TradeBatchError.BagChanged, 0));

        basket.SellLines.Should().ContainSingle().Which.Quantity.Should().Be(20, "nothing was sold, so nothing leaves the basket");
        basket.IsSellPending.Should().BeFalse();
    }

    [Fact]
    public void Sync_NewStockChangesASellPrice_RequiresAReconfirmBeforeSending()
    {
        RecordingTraderSession session = new();
        TradeBasket basket = new();
        ItemSlot[] bag = Bag();
        TraderView before = View();
        basket.AddFromBag(1, bag[1], before, wholeStack: true);
        basket.Sync(before, bag);

        basket.Sync(View(peltPrice: 3), bag);

        basket.SellPricesChanged.Should().BeTrue();
        basket.SellTotal.Should().Be(60, "the basket shows the new price");
        basket.PressSell(session, Trader).Should().Be(TradeBasketStep.ConfirmPrices);
        session.Sent.Should().BeEmpty("the first press only accepts the new total");
        basket.PressSell(session, Trader).Should().Be(TradeBasketStep.Sent);
        session.Sent[0].ExpectedGold.Should().Be(60, "the confirmed total is the new one");
    }

    [Fact]
    public void Apply_ServerSaysPricesChanged_RequiresAReconfirm()
    {
        RecordingTraderSession session = new();
        TradeBasket basket = new();
        ItemSlot[] bag = Bag();
        basket.AddFromBag(1, bag[1], View(), wholeStack: true);
        basket.PressSell(session, Trader);

        basket.Apply(new TradeBatchReply(Trader, TraderBatchResult.KindSell, OpStatus.Conflict, TradeBatchError.PricesChanged, TraderBatchResult.NoLine));

        basket.PressSell(session, Trader).Should().Be(TradeBasketStep.ConfirmPrices);
        session.Sent.Should().ContainSingle("the refused batch only");
    }

    [Fact]
    public void Sync_OwnSuccessfulSaleArrivesBeforeItsAnswer_IsNotAPriceChange()
    {
        RecordingTraderSession session = new();
        TradeBasket basket = new();
        ItemSlot[] bag = Bag();
        basket.AddFromBag(1, bag[1], View(), wholeStack: true);
        basket.Sync(View(), bag);
        basket.PressSell(session, Trader);

        // The stock pushed by the sale itself no longer lists the pelts: that is the sale, not a price change.
        basket.Sync(View(peltPrice: 0), bag);
        basket.Apply(new TradeBatchReply(Trader, TraderBatchResult.KindSell, OpStatus.Ok, TradeBatchError.None, TraderBatchResult.NoLine));

        basket.SellPricesChanged.Should().BeFalse();
        basket.SellLines.Should().BeEmpty();
    }

    [Fact]
    public void PressSell_EnhancedItem_WarnsOnceBeforeSending()
    {
        RecordingTraderSession session = new();
        TradeBasket basket = new();
        ItemSlot[] bag = Bag();
        basket.AddFromBag(2, bag[2], View(), wholeStack: false).Should().BeTrue();

        basket.SellNeedsEnhancedConfirm.Should().BeTrue();
        basket.PressSell(session, Trader).Should().Be(TradeBasketStep.ConfirmEnhanced);
        session.Sent.Should().BeEmpty("an enhanced item is never sold on the first press");
        basket.PressSell(session, Trader).Should().Be(TradeBasketStep.Sent);
        session.Sent[0].Lines[0].UpgradeLevel.Should().Be(4);
    }

    [Fact]
    public void AddFromBag_EquipmentOrUnsellableOrFullyReserved_IsRefused()
    {
        TradeBasket basket = new();
        ItemSlot[] bag = Bag();

        basket.AddFromBag(PlayerInventoryLayout.HeadSlot, new ItemSlot(Sword, 1, 9UL), View(), wholeStack: false).Should().BeFalse("equipped items cannot be added");
        basket.AddFromBag(3, bag[3], View(), wholeStack: false).Should().BeFalse("the trader does not buy it");
        basket.AddFromBag(1, bag[1], View(), wholeStack: true).Should().BeTrue();
        basket.AddFromBag(1, bag[1], View(), wholeStack: false).Should().BeFalse("all 20 are already in the basket");
        basket.AdjustSell(0, 5, bag[1]).Should().BeTrue();
        basket.ReservedAt(1).Should().Be(20, "the quantity control stops at the stack");
    }

    [Fact]
    public void BuyCart_StagesWithoutSending_ThenBuysExactlyTheCart()
    {
        RecordingTraderSession session = new();
        TradeBasket basket = new();
        TraderView view = View();

        basket.AddFromStock(0, view, 1).Should().BeTrue();
        basket.AddFromStock(1, view, 5).Should().BeTrue();
        basket.AddFromStock(1, view, 5).Should().BeTrue();
        session.Sent.Should().BeEmpty();
        basket.InCartAt(1).Should().Be(8, "only 8 are left");

        basket.PressBuy(session, view, gold: 10).Should().Be(TradeBasketStep.NotEnoughGold);
        session.Sent.Should().BeEmpty("a cart the wallet cannot pay is not sent");
        basket.PressBuy(session, view, gold: 1_000).Should().Be(TradeBasketStep.Sent);

        RecordingTraderSession.Batch sent = session.Sent[0];
        sent.Sell.Should().BeFalse();
        sent.Epoch.Should().Be(view.Epoch);
        sent.Lines.Should().Equal(new TradeBasketLine(0, 201U, 1, 40), new TradeBasketLine(1, 202U, 8, 12));
        sent.ExpectedGold.Should().Be(40 + (8 * 12));
        basket.Apply(new TradeBatchReply(Trader, TraderBatchResult.KindBuy, OpStatus.Ok, TradeBatchError.None, TraderBatchResult.NoLine));
        basket.BuyLines.Should().BeEmpty();
    }

    [Fact]
    public void Sync_TraderRestocksWhileTheCartIsOpen_RequiresAReconfirm()
    {
        RecordingTraderSession session = new();
        TradeBasket basket = new();
        TraderView view = View();
        basket.AddFromStock(0, view, 1);
        basket.Sync(view, Bag());

        TraderView restocked = View() with { Epoch = view.Epoch + 1 };
        basket.Sync(restocked, Bag());

        basket.BuyPricesChanged.Should().BeTrue("a restock may have changed what the cart buys");
        basket.PressBuy(session, restocked, 1_000).Should().Be(TradeBasketStep.ConfirmPrices);
        session.Sent.Should().BeEmpty();
        basket.PressBuy(session, restocked, 1_000).Should().Be(TradeBasketStep.Sent);
        session.Sent[0].Epoch.Should().Be(restocked.Epoch, "the cart is confirmed against the new restock");
    }

    [Fact]
    public void Controller_ShiftClickOnABagItem_MovesTheStackToTheBasketAndSellsNothing()
    {
        RecordingTraderSession session = new();
        TradeWindowController window = new(new TradeBasket());
        ItemSlot[] bag = Bag();
        TradeWindowHit pelt = new(TradeWindowTarget.Bag, 1);

        Frame(window, pelt, pressed: true, down: true, shift: true, Vector2.Zero, bag, session);
        Frame(window, pelt, pressed: false, down: false, shift: true, Vector2.Zero, bag, session);

        session.Sent.Should().BeEmpty("Shift+click used to sell the whole stack at once; now it only stages it");
        window.Basket.ReservedAt(1).Should().Be(20);
    }

    [Fact]
    public void Controller_DragABagItemOntoToSell_MovesTheWholeStack_DropElsewhereMovesNothing()
    {
        RecordingTraderSession session = new();
        TradeWindowController window = new(new TradeBasket());
        ItemSlot[] bag = Bag();
        TradeWindowHit crystal = new(TradeWindowTarget.Bag, 0);

        Frame(window, crystal, pressed: true, down: true, shift: false, Vector2.Zero, bag, session);
        Frame(window, TradeWindowHit.None, pressed: false, down: true, shift: false, new Vector2(40f, 0f), bag, session);
        window.DraggedSlot.Should().Be(0);
        Frame(window, TradeWindowHit.None, pressed: false, down: false, shift: false, new Vector2(40f, 0f), bag, session);
        window.Basket.SellLines.Should().BeEmpty("a drop outside the basket moves nothing");

        Frame(window, crystal, pressed: true, down: true, shift: false, Vector2.Zero, bag, session);
        Frame(window, new TradeWindowHit(TradeWindowTarget.SellArea), pressed: false, down: true, shift: false, new Vector2(0f, 300f), bag, session);
        Frame(window, new TradeWindowHit(TradeWindowTarget.SellArea), pressed: false, down: false, shift: false, new Vector2(0f, 300f), bag, session);

        window.Basket.ReservedAt(0).Should().Be(10, "a drop on To sell moves the whole stack");
        session.Sent.Should().BeEmpty();
    }

    [Fact]
    public void Controller_ClickOnAStockRowAndOnSell_StagesThenSendsOnlyOnTheButton()
    {
        RecordingTraderSession session = new();
        TradeWindowController window = new(new TradeBasket());
        ItemSlot[] bag = Bag();

        Frame(window, new TradeWindowHit(TradeWindowTarget.Stock, 1), pressed: true, down: true, shift: true, Vector2.Zero, bag, session);
        Frame(window, new TradeWindowHit(TradeWindowTarget.Bag, 0), pressed: true, down: true, shift: false, Vector2.Zero, bag, session);
        Frame(window, new TradeWindowHit(TradeWindowTarget.Bag, 0), pressed: false, down: false, shift: false, Vector2.Zero, bag, session);
        session.Sent.Should().BeEmpty("clicks on stock and bag only stage");
        window.Basket.InCartAt(1).Should().Be(TradeWindowController.BulkBuy);
        window.Basket.ReservedAt(0).Should().Be(1);

        Frame(window, new TradeWindowHit(TradeWindowTarget.SellButton), pressed: true, down: true, shift: false, Vector2.Zero, bag, session);

        session.Sent.Should().ContainSingle().Which.Sell.Should().BeTrue("Sell commits the basket, not the cart");
    }

    [Fact]
    public void Controller_WindowClosesOrPlayerWalksAway_ReturnsEverythingAndSendsNothing()
    {
        RecordingTraderSession session = new();
        TradeWindowController window = new(new TradeBasket());
        ItemSlot[] bag = Bag();
        window.Basket.AddFromBag(1, bag[1], View(), wholeStack: true);
        window.Basket.AddFromStock(0, View(), 1);
        window.Sync(View(), bag, session);

        window.Sync(active: null, bag, session);

        window.Basket.IsEmpty.Should().BeTrue("closing the window returns the basket and empties the cart");
        window.Basket.ReservedAt(1).Should().Be(0);
        session.Sent.Should().BeEmpty("nothing was ever on the server, so nothing needs undoing");
    }

    [Fact]
    public void Controller_Sync_AppliesTheServersAnswer()
    {
        RecordingTraderSession session = new();
        TradeWindowController window = new(new TradeBasket());
        ItemSlot[] bag = Bag();
        window.Basket.AddFromBag(1, bag[1], View(), wholeStack: true);
        window.Basket.PressSell(session, Trader);
        session.Replies.Enqueue(new TradeBatchReply(Trader, TraderBatchResult.KindSell, OpStatus.Ok, TradeBatchError.None, TraderBatchResult.NoLine));

        window.Sync(View(), bag, session);

        window.Basket.SellLines.Should().BeEmpty();
        window.Basket.IsSellPending.Should().BeFalse();
    }

    private static void Frame(TradeWindowController window, TradeWindowHit hit, bool pressed, bool down, bool shift, Vector2 mouse, ItemSlot[] bag, RecordingTraderSession session) =>
        _ = window.Handle(hit, pressed, down, shift, mouse, bag, View(), 1_000L, session);

    // Bag: 10 crystals (5 gold each), 20 pelts (2 each), a +4 sword (30) and a stone the trader does not buy.
    private static ItemSlot[] Bag()
    {
        ItemSlot[] bag = new ItemSlot[PlayerInventoryLayout.TotalCapacity];
        bag[0] = new ItemSlot(Crystal, 10);
        bag[1] = new ItemSlot(Pelt, 20);
        bag[2] = new ItemSlot(Sword, 1, 42UL, 100f, UpgradeLevel: 4);
        bag[3] = new ItemSlot(999U, 3);
        return bag;
    }

    private static TraderView View(int peltPrice = 2) => new(
        Trader,
        TraderStock.KindMerchant,
        Epoch: 12,
        DateTimeOffset.UnixEpoch,
        ItemIds: [201, 202],
        UnitPrices: [40, 12],
        Stocked: [4, 8],
        Remaining: [4, 8],
        SellItemIds: peltPrice > 0 ? [(ushort)Crystal, (ushort)Pelt, (ushort)Sword] : [(ushort)Crystal, (ushort)Sword],
        SellPrices: peltPrice > 0 ? [5, peltPrice, 30] : [5, 30],
        CanRepair: false);

    /// <summary>Records committed batches instead of sending them; queued replies stand in for the server.</summary>
    private sealed class RecordingTraderSession : ITraderSession
    {
        public sealed record Batch(bool Sell, ulong Trader, long Epoch, TradeBasketLine[] Lines, long ExpectedGold);

        public List<Batch> Sent { get; } = [];

        public Queue<TradeBatchReply> Replies { get; } = new();

        public TraderView? ActiveTrader => View();

        public bool TryOpen(ulong traderNetworkId) => true;

        public bool TrySellBatch(ulong traderNetworkId, ReadOnlySpan<TradeBasketLine> lines, long expectedGold)
        {
            this.Sent.Add(new Batch(true, traderNetworkId, 0, lines.ToArray(), expectedGold));
            return true;
        }

        public bool TryBuyBatch(ulong traderNetworkId, long epoch, ReadOnlySpan<TradeBasketLine> lines, long expectedGold)
        {
            this.Sent.Add(new Batch(false, traderNetworkId, epoch, lines.ToArray(), expectedGold));
            return true;
        }

        public bool TryTakeBatchReply(out TradeBatchReply reply) => this.Replies.TryDequeue(out reply);

        public void Close()
        {
        }
    }
}
