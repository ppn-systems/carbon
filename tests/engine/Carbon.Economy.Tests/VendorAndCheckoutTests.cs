// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Contracts.Common;
using Carbon.Economy.Crafting;
using Carbon.Economy.Currency;
using Carbon.Economy.Vendor;
using Carbon.Items.Catalog;
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

    private static Items.Definitions.ItemCatalogEntry Item(string code) =>
        ShippedConfig.Items.TryGetByCode(code, out Items.Definitions.ItemCatalogEntry? item) ? item : throw new System.Collections.Generic.KeyNotFoundException(code);

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
    public void TryRepair_EnhancedItem_RestoresTheEffectiveMaxForItsLevel()
    {
        // Audit DOM-4: a +5 iron sword has 200 × 1.5 = 300 max durability, so repair at the base 200 is not "full".
        VendorService vendor = new(s_prices);
        Wallet wallet = new(1);
        wallet.TryCredit(CurrencyKind.Gold, 1_000, CurrencyReason.Admin);
        InventoryContainer bag = new(1);
        uint sword = Id("iron_sword");
        float effectiveMax = ItemEnhancementPolicy.GetEffectiveMaxDurability(Item("iron_sword").MaxDurability, 5);
        bag.SetSlot(0, new ItemSlot(sword, 1, InstanceId: 5, Durability: 200f, UpgradeLevel: 5));

        long cost = s_prices.GetRepairCost(bag.GetSlot(0));
        EconomyResult result = vendor.TryRepair(wallet, bag, 0);

        effectiveMax.Should().Be(300f);
        cost.Should().BeGreaterThan(0, "a +5 item at the base maximum is still a third worn");
        result.IsOk.Should().BeTrue();
        wallet.Gold.Should().Be(1_000 - cost);
        bag.GetSlot(0).Durability.Should().Be(effectiveMax, "repair restores the enhanced maximum");
        bag.GetSlot(0).UpgradeLevel.Should().Be(5);
        vendor.TryRepair(wallet, bag, 0).Error.Should().Be(EconomyError.AlreadyRepaired);
    }

    [Fact]
    public void GetRepairCost_SameWear_EpicCostsMorePerGoldOfValueThanCommon()
    {
        // Rarity (economy.json repair.rarityMultiplierPercent) makes high-tier gear the main recurring gold sink:
        // at the same wear, an epic blade must cost a larger share of its value to repair than a common one.
        double commonShare = s_prices.GetRepairCost(Worn("copper_sword", 0.5f)) / (double)Item("copper_sword").BasePrice;
        double epicShare = s_prices.GetRepairCost(Worn("ember_blade", 0.5f)) / (double)Item("ember_blade").BasePrice;

        epicShare.Should().BeGreaterThan(commonShare * 2, "epic repair is priced at a multiple of common repair per gold of value");
    }

    [Fact]
    public void GetRepairCost_ByWear_GrowsWithMissingDurabilityAndIsFreeWhenFull()
    {
        long light = s_prices.GetRepairCost(Worn("ember_blade", 0.25f));
        long heavy = s_prices.GetRepairCost(Worn("ember_blade", 0.75f));
        long broken = s_prices.GetRepairCost(Worn("ember_blade", 1f));

        light.Should().BePositive();
        heavy.Should().BeGreaterThan(light, "more missing durability costs more to restore");
        broken.Should().BeGreaterThan(heavy);
        s_prices.GetRepairCost(Worn("ember_blade", 0f)).Should().Be(0, "nothing to repair");
        s_prices.GetRepairCost(Worn("copper_sword", 0.001f)).Should().BePositive("a scratch still costs at least the minimum");
    }

    [Fact]
    public void GetCraftingFee_SameStation_EpicOutputPaysMorePerGoldOfValue()
    {
        CraftingRecipe common = Recipe("copper_sword");
        CraftingRecipe epic = Recipe("ember_blade");
        common.Station.Should().Be(epic.Station, "compare like with like");

        double commonShare = s_prices.GetCraftingFee(common) / (double)s_prices.GetOutputValue(common);
        double epicShare = s_prices.GetCraftingFee(epic) / (double)s_prices.GetOutputValue(epic);

        s_prices.GetCraftingFee(common).Should().BePositive("station crafting is a gold sink");
        epicShare.Should().BeGreaterThan(commonShare * 2, "crafting fees scale with the rarest output (economy.json craftingFees.rarityMultiplierPercent)");
        s_prices.GetCraftingFee(Recipe("wood_plank")).Should().Be(0, "hand crafting stays free");
    }

    [Fact]
    public void GetBuyPrice_Consumables_ArePricedByBuyerLevelBand()
    {
        uint potion = Id("small_health_potion");
        uint log = Id("wood_log");
        long basePotion = s_prices.GetBuyPrice(potion);

        s_prices.GetBuyPrice(potion, buyerLevel: 1).Should().Be(basePotion);
        s_prices.GetBuyPrice(potion, buyerLevel: 60).Should().BeGreaterThan(basePotion, "high-level buyers pay their band's consumable price");
        s_prices.GetBuyPrice(potion, buyerLevel: 60).Should().BeGreaterThan(s_prices.GetBuyPrice(potion, buyerLevel: 20));
        s_prices.GetBuyPrice(log, buyerLevel: 60).Should().Be(s_prices.GetBuyPrice(log), "only consumables are level-banded");
        s_prices.GetSellPrice(potion).Should().BeLessThan(basePotion, "a high-level price never lets buy-then-sell mint gold");
    }

    private static ItemSlot Worn(string code, float missingFraction)
    {
        Items.Definitions.ItemCatalogEntry item = Item(code);
        return new ItemSlot(item.Id, 1, InstanceId: 1, Durability: item.MaxDurability * (1f - missingFraction));
    }

    [Fact]
    public void TryBuy_WithBuyerLevel_ChargesTheBandedPrice()
    {
        VendorService vendor = new(s_prices);
        Wallet wallet = new(1);
        wallet.TryCredit(CurrencyKind.Gold, 10_000, CurrencyReason.Admin);
        InventoryContainer bag = new(4);
        uint potion = Id("small_health_potion");

        vendor.TryBuy(wallet, bag, potion, 3, buyerLevel: 80).IsOk.Should().BeTrue();

        wallet.Gold.Should().Be(10_000 - (3 * s_prices.GetBuyPrice(potion, buyerLevel: 80)));
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
