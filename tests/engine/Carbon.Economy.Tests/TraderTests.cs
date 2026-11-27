// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using Carbon.Contracts.Common;
using Carbon.Economy.Currency;
using Carbon.Economy.Trading;
using Carbon.Economy.Vendor;
using Carbon.Items.Containers;
using FluentAssertions;
using Xunit;

namespace Carbon.Economy.Tests;

public sealed class TraderTests
{
    private const ulong Seed = 2026UL;
    private const long Now = 1_800_000_000L;

    private static readonly Lazy<TraderCatalog> s_catalog = new(() => TraderCatalog.LoadFromJson(ShippedConfig.Read("traders.json"), ShippedConfig.Items, ShippedConfig.Economy));
    private static readonly Lazy<PriceBook> s_prices = new(() => new PriceBook(ShippedConfig.Items, ShippedConfig.Economy));

    private static TraderCatalog Catalog => s_catalog.Value;

    private static PriceBook Prices => s_prices.Value;

    [Fact]
    public void LoadFromJson_ShippedConfig_HasTheThreeProfiles()
    {
        Catalog.TryGet("merchant", out _).Should().BeTrue();
        Catalog.TryGet("general", out _).Should().BeTrue();
        Catalog.TryGet("blacksmith", out TraderProfile smith).Should().BeTrue();
        smith.Entries.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("""{ "profiles": [ { "id": "a", "slots": 1, "priceVariancePercent": 10, "restockMinutes": 30, "entries": [ { "item": "no_such_item", "min": 1, "max": 1 } ] } ] }""")]
    [InlineData("""{ "profiles": [ { "id": "a", "slots": 1, "priceVariancePercent": 60, "restockMinutes": 30, "entries": [ { "item": "bread", "min": 1, "max": 1 } ] } ] }""")]
    [InlineData("""{ "maxPriceVariancePercent": 80, "profiles": [] }""")]
    [InlineData("""{ "profiles": [ { "id": "a", "slots": 1, "priceVariancePercent": 10, "restockMinutes": 30, "entries": [ { "item": "bread", "min": 3, "max": 2 } ] } ] }""")]
    [InlineData("""{ "profiles": [ { "id": "a", "slots": 2, "priceVariancePercent": 10, "restockMinutes": 30, "entries": [ { "item": "bread", "min": 1, "max": 2 } ] } ] }""")]
    [InlineData("""{ "profiles": [ { "id": "a", "slots": 1, "priceVariancePercent": 10, "restockMinutes": 30, "restockJitterMinutes": 30, "entries": [ { "item": "bread", "min": 1, "max": 2 } ] } ] }""")]
    [InlineData("""{ "profiles": [ { "id": "a", "slots": 1, "priceVariancePercent": 10, "restockMinutes": 30, "entries": [ { "item": "bread", "min": 1, "max": 2, "styles": ["moon"] } ] } ] }""")]
    public void LoadFromJson_InvalidDocument_IsRejected(string json)
    {
        Action load = () => TraderCatalog.LoadFromJson(json, ShippedConfig.Items, ShippedConfig.Economy);
        load.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Roll_SameEpoch_IsDeterministic_AndRestockChangesIt()
    {
        Catalog.TryGet("merchant", out TraderProfile profile);
        Span<TraderStockSlot> a = stackalloc TraderStockSlot[TraderCatalog.MaxSlots];
        Span<TraderStockSlot> b = stackalloc TraderStockSlot[TraderCatalog.MaxSlots];
        int na = TraderStockRoller.Roll(profile, Seed, 77UL, 1000L, 0, a);
        int nb = TraderStockRoller.Roll(profile, Seed, 77UL, 1000L, 0, b);
        na.Should().Be(profile.Slots);
        a[..na].SequenceEqual(b[..nb]).Should().BeTrue("the same (seed, trader, epoch) always rolls the same stock");

        int changed = 0;
        for (long epoch = 1001; epoch < 1021; epoch++)
        {
            int n = TraderStockRoller.Roll(profile, Seed, 77UL, epoch, 0, b);
            changed += a[..na].SequenceEqual(b[..n]) ? 0 : 1;
        }

        changed.Should().BeGreaterThan(17, "restocks roll new stock");
        TraderStockRoller.Roll(profile, Seed, 78UL, 1000L, 0, b);
        a[..na].SequenceEqual(b[..na]).Should().BeFalse("two villages stock differently");
    }

    [Fact]
    public void Roll_Slots_AreDistinctAndRespectStyleAndQuantityRanges()
    {
        Span<TraderStockSlot> slots = stackalloc TraderStockSlot[TraderCatalog.MaxSlots];
        foreach (TraderProfile profile in Catalog.Profiles)
        {
            for (byte style = 0; style < TraderCatalog.StyleNames.Count; style++)
            {
                for (long epoch = 0; epoch < 50; epoch++)
                {
                    int n = TraderStockRoller.Roll(profile, Seed, (ulong)epoch * 31UL, epoch, style, slots);
                    n.Should().BeGreaterThan(0);
                    HashSet<uint> ids = [];
                    for (int i = 0; i < n; i++)
                    {
                        ids.Add(slots[i].ItemId).Should().BeTrue("an item is stocked in one slot only");
                        TraderStockEntry entry = Find(profile, slots[i].ItemId);
                        entry.AppliesTo(style).Should().BeTrue($"{profile.Id} only stocks style-appropriate goods");
                        slots[i].Quantity.Should().BeInRange(entry.MinQuantity, entry.MaxQuantity);
                        slots[i].PriceOffsetPercent.Should().BeInRange(-profile.PriceVariancePercent, profile.PriceVariancePercent);
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(80)]
    public void UnitPrice_StaysWithinVarianceAndAboveTheBuyBackPrice(int level)
    {
        foreach (TraderProfile profile in Catalog.Profiles)
        {
            foreach (TraderStockEntry entry in profile.Entries)
            {
                long list = Prices.GetListPrice(entry.ItemId, level);
                foreach (int offset in new[] { -profile.PriceVariancePercent, 0, profile.PriceVariancePercent })
                {
                    long price = TraderStockRoller.UnitPrice(Prices, new TraderStockSlot(entry.ItemId, 1, offset), level);
                    price.Should().BeGreaterThan(Prices.GetSellPrice(entry.ItemId), "buy-then-sell never mints gold");
                    price.Should().BeGreaterThanOrEqualTo((long)Math.Floor(list * (100 - profile.PriceVariancePercent) / 100.0));
                    price.Should().BeLessThanOrEqualTo(Math.Max(Prices.GetSellPrice(entry.ItemId) + 1, (long)Math.Ceiling(list * (100 + profile.PriceVariancePercent) / 100.0)));
                }
            }
        }
    }

    [Fact]
    public void Clock_EpochAdvancesAtTheRestock_AndJitterDiffersPerTrader()
    {
        Catalog.TryGet("merchant", out TraderProfile profile);
        long epoch = TraderClock.EpochAt(5UL, profile, Now);
        int left = TraderClock.SecondsUntilRestock(5UL, profile, Now);
        left.Should().BeInRange(1, profile.RestockSeconds);
        TraderClock.EpochAt(5UL, profile, Now + left - 1).Should().Be(epoch);
        TraderClock.EpochAt(5UL, profile, Now + left).Should().Be(epoch + 1);
        TraderClock.SecondsUntilRestock(5UL, profile, Now + left).Should().Be(profile.RestockSeconds);

        HashSet<long> jitters = [];
        for (ulong key = 0; key < 20; key++)
        {
            long j = TraderClock.JitterOf(key, profile);
            j.Should().BeInRange(0, profile.RestockJitterSeconds);
            jitters.Add(j);
        }

        jitters.Count.Should().BeGreaterThan(10, "villages restock at different moments");
    }

    [Fact]
    public void TryBuy_LimitedQuantity_SellsOutAndRestocksNextEpoch()
    {
        TraderBook book = new(Seed);
        TraderIdentity trader = new(9UL, "general", 0);
        (TraderOffer offer, long epoch) = FirstOffer(book, trader, Now);
        Wallet wallet = RichWallet();
        InventoryContainer bag = new(48);

        book.TryBuy(Catalog, Prices, trader, Now, wallet, bag, 0, offer.Remaining, epoch, 10, 40).Should().Be(TradeResult.Ok);
        bag.CountItem(offer.ItemId).Should().Be(offer.Remaining);
        wallet.Gold.Should().Be(1_000_000L - (offer.UnitPrice * offer.Remaining));
        book.TryBuy(Catalog, Prices, trader, Now, wallet, bag, 0, 1, epoch, 10, 40).Should().Be(TradeResult.SoldOut);
        (TraderOffer after, _) = FirstOffer(book, trader, Now);
        after.Remaining.Should().Be(0);

        long later = Now + TraderClock.SecondsUntilRestock(trader.Key, Profile("general"), Now);
        book.TryBuy(Catalog, Prices, trader, later, wallet, bag, 0, 1, epoch, 10, 40).Should().Be(TradeResult.StaleEpoch, "a restock changes what a click buys");
        Span<TraderOffer> fresh = stackalloc TraderOffer[TraderCatalog.MaxSlots];
        int n = book.GetOffers(Catalog, Prices, trader, later, 10, fresh, out long nextEpoch, out _);
        nextEpoch.Should().Be(epoch + 1);
        for (int i = 0; i < n; i++)
        {
            fresh[i].Remaining.Should().Be(fresh[i].Stocked, "the new epoch is fully stocked");
        }
    }

    [Fact]
    public void TryBuy_Failures_ChangeNothing()
    {
        TraderBook book = new(Seed);
        TraderIdentity trader = new(10UL, "blacksmith", 2);
        (TraderOffer offer, long epoch) = FirstOffer(book, trader, Now);

        Wallet poor = new(1UL);
        InventoryContainer bag = new(48);
        book.TryBuy(Catalog, Prices, trader, Now, poor, bag, 0, 1, epoch, 10, 40).Should().Be(TradeResult.InsufficientFunds);
        bag.CountItem(offer.ItemId).Should().Be(0);

        Wallet rich = RichWallet();
        InventoryContainer full = new(48);
        for (int i = 0; i < 40; i++)
        {
            full.SetSlot(i, new ItemSlot(1U, 1));
        }

        book.TryBuy(Catalog, Prices, trader, Now, rich, full, 0, 1, epoch, 10, 40).Should().Be(TradeResult.InventoryFull);
        rich.Gold.Should().Be(1_000_000L, "a full bag is never charged");
        book.TryBuy(Catalog, Prices, trader, Now, rich, bag, 99, 1, epoch, 10, 40).Should().Be(TradeResult.InvalidRequest);
        book.TryBuy(Catalog, Prices, trader, Now, rich, bag, 0, 0, epoch, 10, 40).Should().Be(TradeResult.InvalidRequest);
        book.TryBuy(Catalog, Prices, trader, Now, rich, bag, 0, 1, epoch - 1, 10, 40).Should().Be(TradeResult.StaleEpoch);
        book.TryBuy(Catalog, Prices, new TraderIdentity(10UL, "nobody", 0), Now, rich, bag, 0, 1, epoch, 10, 40).Should().Be(TradeResult.UnknownTrader);
        rich.Gold.Should().Be(1_000_000L);
        FirstOffer(book, trader, Now).Offer.Remaining.Should().Be(offer.Remaining, "failed purchases sell nothing");
        TradeResult.SoldOut.ToOpStatus().Should().Be(OpStatus.Conflict);
        TradeResult.InventoryFull.ToOpStatus().Should().Be(OpStatus.InventoryFull);
    }

    [Fact]
    public void Sales_DrainAndImport_RestoreRemainingStock()
    {
        TraderBook book = new(Seed);
        TraderIdentity trader = new(11UL, "merchant", 1);
        (TraderOffer offer, long epoch) = FirstOffer(book, trader, Now);
        book.TryBuy(Catalog, Prices, trader, Now, RichWallet(), new InventoryContainer(48), 0, 1, epoch, 10, 40).Should().Be(TradeResult.Ok);

        List<TraderSalesRecord> drained = [];
        book.DrainDirty(drained);
        drained.Should().ContainSingle();
        List<TraderSalesRecord> again = [];
        book.DrainDirty(again);
        again.Should().BeEmpty("only changes are persisted");

        TraderBook restored = new(Seed);
        restored.Import(drained[0]);
        FirstOffer(restored, trader, Now).Offer.Remaining.Should().Be(offer.Remaining - 1, "sales survive a restart");
    }

    private static Wallet RichWallet()
    {
        Wallet wallet = new(1UL);
        wallet.TryCredit(CurrencyKind.Gold, 1_000_000L, CurrencyReason.VendorSale).IsOk.Should().BeTrue();
        return wallet;
    }

    private static TraderProfile Profile(string id)
    {
        Catalog.TryGet(id, out TraderProfile profile).Should().BeTrue();
        return profile;
    }

    private static (TraderOffer Offer, long Epoch) FirstOffer(TraderBook book, in TraderIdentity trader, long now)
    {
        Span<TraderOffer> offers = stackalloc TraderOffer[TraderCatalog.MaxSlots];
        int n = book.GetOffers(Catalog, Prices, trader, now, 10, offers, out long epoch, out _);
        n.Should().BeGreaterThan(0);
        return (offers[0], epoch);
    }

    private static TraderStockEntry Find(TraderProfile profile, uint itemId)
    {
        foreach (TraderStockEntry e in profile.Entries)
        {
            if (e.ItemId == itemId)
            {
                return e;
            }
        }

        throw new InvalidOperationException("item not in profile");
    }
}
