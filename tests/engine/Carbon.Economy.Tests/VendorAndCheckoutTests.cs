// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Contracts.Common;
using Carbon.Economy.Crafting;
using Carbon.Economy.Currency;
using Carbon.Economy.Vendor;
using Carbon.Items.Components;
using Carbon.Items.Containers;
using Carbon.Items.Crafting;
using FluentAssertions;
using Xunit;

namespace Carbon.Economy.Tests;

public sealed class VendorAndCheckoutTests
{
    private static readonly PriceBook s_prices = new(ShippedConfig.Items, ShippedConfig.Economy);

    private static uint Id(string code) => ShippedConfig.Items.ResolveId(code);

    private static CraftingRecipe Recipe(string code) =>
        ShippedConfig.Recipes.TryGetByCode(code, out CraftingRecipe? recipe) ? recipe : throw new System.Collections.Generic.KeyNotFoundException(code);

    [Fact]
    public void TryBuy_WithEnoughGold_DebitsAndAddsItems()
    {
        VendorService vendor = new(s_prices);
        Wallet wallet = new(1);
        wallet.TryCredit(CurrencyKind.Gold, 100, CurrencyReason.Admin);
        InventoryContainer bag = new(4);

        EconomyResult result = vendor.TryBuy(wallet, bag, Id("wood_log"), 10);

        result.IsOk.Should().BeTrue();
        wallet.Gold.Should().Be(100 - (10 * s_prices.GetBuyPrice(Id("wood_log"))));
        bag.CountItem(Id("wood_log")).Should().Be(10);
    }

    [Fact]
    public void TryBuy_WhenInventoryFull_ChargesNothing()
    {
        VendorService vendor = new(s_prices);
        Wallet wallet = new(1);
        wallet.TryCredit(CurrencyKind.Gold, 1_000, CurrencyReason.Admin);
        InventoryContainer bag = new(1);
        bag.TryAdd(new ItemSlot(Id("stone_chunk"), 99));

        EconomyResult result = vendor.TryBuy(wallet, bag, Id("wood_log"), 1);

        result.Status.Should().Be(OpStatus.InventoryFull);
        wallet.Gold.Should().Be(1_000);
    }

    [Fact]
    public void TryBuy_WithInsufficientGoldOrUnbuyableItem_ChangesNothing()
    {
        VendorService vendor = new(s_prices);
        Wallet wallet = new(1);
        wallet.TryCredit(CurrencyKind.Gold, 5, CurrencyReason.Admin);
        InventoryContainer bag = new(4);

        vendor.TryBuy(wallet, bag, Id("wood_log"), 50).Error.Should().Be(EconomyError.InsufficientFunds);
        vendor.TryBuy(wallet, bag, Id("demon_horn"), 1).Error.Should().Be(EconomyError.NotForSale);
        vendor.TryBuy(wallet, bag, 999_999, 1).Error.Should().Be(EconomyError.UnknownItem);
        vendor.TryBuy(wallet, bag, Id("wood_log"), 0).Error.Should().Be(EconomyError.InvalidAmount);
        wallet.Gold.Should().Be(5);
        bag.OccupiedSlotCount.Should().Be(0);
    }

    [Fact]
    public void TrySell_PaysSpreadReducedPriceAndRemovesItems()
    {
        VendorService vendor = new(s_prices);
        Wallet wallet = new(1);
        InventoryContainer bag = new(4);
        bag.TryAdd(new ItemSlot(Id("orc_tusk"), 10));

        vendor.TrySell(wallet, bag, Id("orc_tusk"), 11).Error.Should().Be(EconomyError.InsufficientItems);
        vendor.TrySell(wallet, bag, Id("orc_tusk"), 10).IsOk.Should().BeTrue();

        wallet.Gold.Should().Be(10 * s_prices.GetSellPrice(Id("orc_tusk")));
        s_prices.GetSellPrice(Id("orc_tusk")).Should().Be(15 * ShippedConfig.Economy.Vendor.SellRatioPercent / 100, "unit sell price is floor(basePrice × sellRatio)");
        bag.CountItem(Id("orc_tusk")).Should().Be(0);
    }

    [Fact]
    public void TrySell_WhenWalletAtCap_KeepsItems()
    {
        VendorService vendor = new(s_prices);
        Wallet wallet = new(1, goldCap: 10);
        wallet.TryCredit(CurrencyKind.Gold, 10, CurrencyReason.Admin);
        InventoryContainer bag = new(4);
        bag.TryAdd(new ItemSlot(Id("orc_tusk"), 5));

        vendor.TrySell(wallet, bag, Id("orc_tusk"), 5).Error.Should().Be(EconomyError.BalanceCapExceeded);
        bag.CountItem(Id("orc_tusk")).Should().Be(5);
    }

    [Fact]
    public void TryRepair_ChargesProportionalCostAndRestoresDurability()
    {
        VendorService vendor = new(s_prices);
        Wallet wallet = new(1);
        wallet.TryCredit(CurrencyKind.Gold, 1_000, CurrencyReason.Admin);
        InventoryContainer bag = new(2);
        uint sword = Id("iron_sword");
        bag.SetSlot(0, new ItemSlot(sword, 1, InstanceId: 5, Durability: 100f));

        long expected = s_prices.GetRepairCost(bag.GetSlot(0));
        EconomyResult result = vendor.TryRepair(wallet, bag, 0);

        expected.Should().Be(17, "110g sword at half durability → ceil(110 × 30% × 0.5)");
        result.IsOk.Should().BeTrue();
        wallet.Gold.Should().Be(1_000 - expected);
        bag.GetSlot(0).Durability.Should().Be(200f);
        vendor.TryRepair(wallet, bag, 0).Error.Should().Be(EconomyError.AlreadyRepaired);
        vendor.TryRepair(wallet, bag, 1).Error.Should().Be(EconomyError.NotRepairable);
    }

    [Fact]
    public void CraftingCheckout_ChargesStationFee_AndRejectsWithoutFundsAtomically()
    {
        CraftingCheckout checkout = new(s_prices);
        CraftingRecipe ingot = Recipe("iron_ingot");
        long fee = s_prices.GetCraftingFee(ingot);
        CraftContext atForge = new(CraftingStation.Forge, CharacterLevel: 20, SkillLevel: 10);
        InventoryContainer bag = new(4);
        bag.TryAdd(new ItemSlot(Id("iron_ore"), 2));
        bag.TryAdd(new ItemSlot(Id("coal"), 1));
        Wallet broke = new(1);

        fee.Should().BeGreaterThan(0, "forge use is a gold sink");
        checkout.TryCraft(broke, bag, ingot, atForge).Failure.Should().Be(CraftFailure.InsufficientFunds);
        bag.CountItem(Id("iron_ore")).Should().Be(2, "nothing is consumed when the fee cannot be paid");

        Wallet wallet = new(2);
        wallet.TryCredit(CurrencyKind.Gold, 100, CurrencyReason.Admin);
        checkout.TryCraft(wallet, bag, ingot, atForge).IsOk.Should().BeTrue();
        wallet.Gold.Should().Be(100 - fee);
        bag.CountItem(Id("iron_ingot")).Should().Be(1);
        bag.CountItem(Id("iron_ore")).Should().Be(0);
    }

    [Fact]
    public void CraftingCheckout_TryStartJob_ConsumesIngredientsAndReturnsSkillScaledJob()
    {
        CraftingCheckout checkout = new(s_prices);
        CraftingRecipe stew = Recipe("stew");
        InventoryContainer bag = new(6);
        bag.TryAdd(new ItemSlot(Id("raw_meat"), 2));
        bag.TryAdd(new ItemSlot(Id("berries"), 2));
        bag.TryAdd(new ItemSlot(Id("wheat"), 1));
        Wallet wallet = new(1);
        wallet.TryCredit(CurrencyKind.Gold, 50, CurrencyReason.Admin);

        CraftResult wrongPlace = checkout.TryStartJob(wallet, bag, stew, new CraftContext(CraftingStation.Forge, 20, 20), out _);
        wrongPlace.Failure.Should().Be(CraftFailure.WrongStation);

        CraftResult result = checkout.TryStartJob(wallet, bag, stew, new CraftContext(CraftingStation.Stove, 20, 25), out CraftingJobComponent job);

        result.IsOk.Should().BeTrue();
        bag.CountItem(Id("raw_meat")).Should().Be(0);
        bag.CountItem(Id("stew")).Should().Be(0, "outputs arrive when the work completes");
        job.RecipeId.Should().Be(stew.Id);
        job.WorkRequired.Should().Be(stew.WorkTicks * CraftingWork.BaselineUnitsPerTick);
        job.WorkPerTick.Should().Be(CraftingWork.UnitsPerTick(25));
        wallet.Gold.Should().Be(50 - s_prices.GetCraftingFee(stew));
    }
}
