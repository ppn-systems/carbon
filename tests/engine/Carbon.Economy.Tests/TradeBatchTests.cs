// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Contracts.Common;
using Carbon.Economy.Currency;
using Carbon.Economy.Trading;
using Carbon.Economy.Vendor;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Primitives.Random;
using FluentAssertions;
using Xunit;

namespace Carbon.Economy.Tests;

/// <summary>Trader baskets and carts are applied whole or not at all (ADR-025 D18).</summary>
public sealed class TradeBatchTests
{
    private const long Now = 1_800_000_000L;
    private const int Bag = PlayerInventoryLayout.BagLimit;

    private static readonly GameplayKey s_stockKey = TestGameplayKeys.Key(2026UL);
    private static readonly PriceBook s_prices = new(ShippedConfig.Items, ShippedConfig.Economy);
    private static readonly Lazy<TraderCatalog> s_catalog = new(() => TraderCatalog.LoadFromJson(ShippedConfig.Read("traders.json"), ShippedConfig.Items, ShippedConfig.Economy));

    private static uint Crystal => ShippedConfig.Items.ResolveId("mana_crystal");

    private static uint Pelt => ShippedConfig.Items.ResolveId("beast_pelt");

    [Fact]
    public void TrySellBatch_ValidBasket_PaysExactlyTheTotalAndRemovesExactlyTheLines()
    {
        VendorService vendor = new(s_prices);
        Wallet wallet = new(1);
        InventoryContainer inventory = SellerInventory();
        long total = (7 * s_prices.GetSellPrice(Crystal)) + (20 * s_prices.GetSellPrice(Pelt));
        total.Should().BePositive("both items sell");

        TradeBatchOutcome outcome = vendor.TrySellBatch(wallet, inventory, [new(0, Crystal, 7), new(3, Pelt, 20)], Bag, total);

        outcome.IsOk.Should().BeTrue();
        outcome.Gold.Should().Be(total);
        wallet.Gold.Should().Be(total, "the wallet gains exactly the confirmed total");
        inventory.GetSlot(0).Quantity.Should().Be(3, "7 of the 10 logs were sold");
        inventory.GetSlot(3).IsEmpty.Should().BeTrue("the whole stone stack was sold");
        inventory.GetSlot(5).Should().Be(new ItemSlot(Crystal, 4), "a slot outside the basket is untouched");
    }

    [Theory]
    [InlineData(0, false, 11, TradeBatchError.BagChanged)]
    [InlineData(5, true, 1, TradeBatchError.BagChanged)]
    [InlineData(1, false, 1, TradeBatchError.BagChanged)]
    [InlineData(Bag, false, 1, TradeBatchError.InvalidRequest)]
    [InlineData(0, false, 0, TradeBatchError.InvalidRequest)]
    [InlineData(3, false, 1, TradeBatchError.DuplicateLine)]
    public void TrySellBatch_OneBadLine_SellsNothing(int slot, bool otherItem, int quantity, TradeBatchError expected)
    {
        VendorService vendor = new(s_prices);
        Wallet wallet = new(1);
        InventoryContainer inventory = SellerInventory();
        ItemSlot[] before = inventory.Slots.ToArray();

        // The good line comes first, so a half-applied batch would show.
        // otherItem: the slot now holds something else than the player saw (a slot moved after it was added).
        uint seen = slot == 3 || otherItem ? Pelt : Crystal;
        TradeLine bad = new(slot, seen, quantity);
        TradeBatchOutcome outcome = vendor.TrySellBatch(wallet, inventory, [new(3, Pelt, 5), bad], Bag, 5 * s_prices.GetSellPrice(Pelt));

        outcome.Error.Should().Be(expected);
        outcome.Line.Should().Be(1, "the second line is to blame");
        wallet.Gold.Should().Be(0, "nothing was paid");
        inventory.Slots.ToArray().Should().Equal(before, "nothing left the bag");
    }

    [Fact]
    public void TrySellBatch_EquippedSlot_IsRefused()
    {
        VendorService vendor = new(s_prices);
        Wallet wallet = new(1);
        InventoryContainer inventory = new(PlayerInventoryLayout.TotalCapacity);
        ItemSlot helmet = new(StarterGameCatalog.IronSword, 1, 5UL, Durability: 100f);
        inventory.SetSlot(PlayerInventoryLayout.HeadSlot, helmet);

        TradeBatchOutcome outcome = vendor.TrySellBatch(wallet, inventory, [new(PlayerInventoryLayout.HeadSlot, helmet.ItemId, 1)], Bag, s_prices.GetSellPrice(helmet.ItemId));

        outcome.Error.Should().Be(TradeBatchError.InvalidRequest, "equipment is never sold");
        inventory.GetSlot(PlayerInventoryLayout.HeadSlot).Should().Be(helmet);
        wallet.Gold.Should().Be(0);
    }

    [Fact]
    public void TrySellBatch_TotalDiffersFromTheConfirmedOne_IsRefusedAsPricesChanged()
    {
        VendorService vendor = new(s_prices);
        Wallet wallet = new(1);
        InventoryContainer inventory = SellerInventory();
        long total = 7 * s_prices.GetSellPrice(Crystal);

        TradeBatchOutcome outcome = vendor.TrySellBatch(wallet, inventory, [new(0, Crystal, 7)], Bag, total + 1);

        outcome.Error.Should().Be(TradeBatchError.PricesChanged, "the player must re-confirm a different total");
        wallet.Gold.Should().Be(0);
        inventory.GetSlot(0).Quantity.Should().Be(10);
    }

    [Fact]
    public void TryBuyBatch_ValidCart_SpendsExactlyTheTotalAndAddsEveryLine()
    {
        TraderBook book = new(s_stockKey);
        TraderIdentity trader = new(21UL, "general", 0);
        (TraderOffer[] offers, long epoch) = Offers(book, trader);
        offers.Length.Should().BeGreaterThan(1);
        Wallet wallet = RichWallet();
        InventoryContainer bag = new(PlayerInventoryLayout.TotalCapacity);
        long total = offers[0].UnitPrice + (2 * offers[1].UnitPrice);

        TradeBatchOutcome outcome = book.TryBuyBatch(s_catalog.Value, s_prices, trader, Now, wallet, bag, [new(0, offers[0].ItemId, 1), new(1, offers[1].ItemId, 2)], epoch, total, 10, Bag);

        outcome.IsOk.Should().BeTrue();
        wallet.Gold.Should().Be(1_000_000L - total);
        bag.CountItem(offers[0].ItemId).Should().Be(offers[0].ItemId == offers[1].ItemId ? 3 : 1);
        bag.CountItem(offers[1].ItemId).Should().Be(offers[0].ItemId == offers[1].ItemId ? 3 : 2);
        (TraderOffer[] after, _) = Offers(book, trader);
        after[0].Remaining.Should().Be(offers[0].Remaining - 1);
        after[1].Remaining.Should().Be(offers[1].Remaining - 2);
    }

    [Fact]
    public void TryBuyBatch_OneLineSoldOut_BuysNothing()
    {
        TraderBook book = new(s_stockKey);
        TraderIdentity trader = new(22UL, "general", 0);
        (TraderOffer[] offers, long epoch) = Offers(book, trader);
        Wallet wallet = RichWallet();
        InventoryContainer bag = new(PlayerInventoryLayout.TotalCapacity);
        int tooMany = offers[1].Remaining + 1;
        long total = offers[0].UnitPrice + (tooMany * offers[1].UnitPrice);

        TradeBatchOutcome outcome = book.TryBuyBatch(s_catalog.Value, s_prices, trader, Now, wallet, bag, [new(0, offers[0].ItemId, 1), new(1, offers[1].ItemId, tooMany)], epoch, total, 10, Bag);

        outcome.Error.Should().Be(TradeBatchError.SoldOut);
        outcome.Line.Should().Be(1);
        wallet.Gold.Should().Be(1_000_000L, "nothing was charged");
        bag.CountItem(offers[0].ItemId).Should().Be(0, "the first line was not bought either");
        Offers(book, trader).Offers[0].Remaining.Should().Be(offers[0].Remaining, "no unit was sold");
    }

    [Fact]
    public void TryBuyBatch_AfterARestockOrWithAnotherTotal_IsRefusedAsPricesChanged()
    {
        TraderBook book = new(s_stockKey);
        TraderIdentity trader = new(23UL, "general", 0);
        (TraderOffer[] offers, long epoch) = Offers(book, trader);
        Wallet wallet = RichWallet();
        InventoryContainer bag = new(PlayerInventoryLayout.TotalCapacity);
        TradeLine[] cart = [new(0, offers[0].ItemId, 1)];
        CatalogProfile("general", out TraderProfile profile);
        long later = Now + TraderClock.SecondsUntilRestock(trader.Key, profile, Now);

        book.TryBuyBatch(s_catalog.Value, s_prices, trader, later, wallet, bag, cart, epoch, offers[0].UnitPrice, 10, Bag)
            .Error.Should().Be(TradeBatchError.PricesChanged, "a restock while the cart was open needs a re-confirm");
        book.TryBuyBatch(s_catalog.Value, s_prices, trader, Now, wallet, bag, cart, epoch - 1, offers[0].UnitPrice, 10, Bag)
            .Error.Should().Be(TradeBatchError.PricesChanged, "a cart priced in another restock is refused even when its lines still match");
        book.TryBuyBatch(s_catalog.Value, s_prices, trader, Now, wallet, bag, cart, epoch, offers[0].UnitPrice - 1, 10, Bag)
            .Error.Should().Be(TradeBatchError.PricesChanged, "the server recomputes the total");
        wallet.Gold.Should().Be(1_000_000L);
        bag.CountItem(offers[0].ItemId).Should().Be(0);
    }

    [Fact]
    public void TryBuyBatch_NotEnoughGoldOrBagSpace_BuysNothing()
    {
        TraderBook book = new(s_stockKey);
        TraderIdentity trader = new(24UL, "general", 0);
        (TraderOffer[] offers, long epoch) = Offers(book, trader);
        TradeLine[] cart = [new(0, offers[0].ItemId, 1), new(1, offers[1].ItemId, 1)];
        long total = offers[0].UnitPrice + offers[1].UnitPrice;

        Wallet poor = new(1);
        poor.TryCredit(CurrencyKind.Gold, total - 1, CurrencyReason.Admin).IsOk.Should().BeTrue();
        InventoryContainer poorBag = new(PlayerInventoryLayout.TotalCapacity);
        book.TryBuyBatch(s_catalog.Value, s_prices, trader, Now, poor, poorBag, cart, epoch, total, 10, Bag)
            .Error.Should().Be(TradeBatchError.NotEnoughGold);
        poor.Gold.Should().Be(total - 1);
        poorBag.CountItem(offers[0].ItemId).Should().Be(0, "an unpaid cart adds nothing to the bag");

        Wallet rich = RichWallet();
        InventoryContainer full = new(PlayerInventoryLayout.TotalCapacity);
        for (int i = 0; i < Bag - 1; i++)
        {
            full.SetSlot(i, new ItemSlot(Pelt, 1, (ulong)(i + 1)));
        }

        book.TryBuyBatch(s_catalog.Value, s_prices, trader, Now, rich, full, cart, epoch, total, 10, Bag)
            .Error.Should().Be(TradeBatchError.BagFull, "one free slot holds only the first line");
        rich.Gold.Should().Be(1_000_000L);
        full.GetSlot(Bag - 1).IsEmpty.Should().BeTrue("the first line was not added either");
    }

    private static void CatalogProfile(string id, out TraderProfile profile) =>
        s_catalog.Value.TryGet(id, out profile).Should().BeTrue();

    private static InventoryContainer SellerInventory()
    {
        InventoryContainer inventory = new(PlayerInventoryLayout.TotalCapacity);
        inventory.SetSlot(0, new ItemSlot(Crystal, 10));
        inventory.SetSlot(3, new ItemSlot(Pelt, 20));
        inventory.SetSlot(5, new ItemSlot(Crystal, 4));
        return inventory;
    }

    private static Wallet RichWallet()
    {
        Wallet wallet = new(1UL);
        wallet.TryCredit(CurrencyKind.Gold, 1_000_000L, CurrencyReason.Admin).IsOk.Should().BeTrue();
        return wallet;
    }

    private static (TraderOffer[] Offers, long Epoch) Offers(TraderBook book, in TraderIdentity trader)
    {
        Span<TraderOffer> offers = stackalloc TraderOffer[TraderCatalog.MaxSlots];
        int n = book.GetOffers(s_catalog.Value, s_prices, trader, Now, 10, offers, out long epoch, out _);
        n.Should().BeGreaterThan(1);
        return (offers[..n].ToArray(), epoch);
    }
}
