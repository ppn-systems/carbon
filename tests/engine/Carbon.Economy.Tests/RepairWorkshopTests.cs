// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Contracts.Common;
using Carbon.Economy.Currency;
using Carbon.Economy.Repair;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Repair;
using FluentAssertions;
using Xunit;

namespace Carbon.Economy.Tests;

/// <summary>
/// One repair (ADR-028): every check runs before any spend, success spends exactly the quote's materials and fee, and
/// only the selected slot's durability changes. Values are pinned from the shipped <c>repair.json</c> by hand.
/// </summary>
public sealed class RepairWorkshopTests
{
    private const int SwordSlot = 0;
    private const int OtherSwordSlot = 1;
    private const int IngotSlot = 2;
    private static readonly ItemRules s_rules = ItemRules.LoadShippedDefaults();

    private static RepairTable Table => s_rules.Repair;

    private static uint Sword => s_rules.ItemCatalog.ResolveId("iron_sword");

    private static uint Ingot => s_rules.ItemCatalog.ResolveId("iron_ingot");

    [Fact]
    public void TryRepair_FullAtTheBlacksmith_SpendsTwoIngotsAndSeventeenGoldAndRestoresTheSword()
    {
        (InventoryContainer bag, Wallet wallet) = Kit(swordDurability: 100f, ingots: 5, gold: 1_000);

        RepairAttempt attempt = RepairWorkshop.TryRepair(Table, wallet, bag, SwordSlot, Sword, 5, RepairVenue.Blacksmith, Table.BlacksmithTier);

        attempt.Error.Should().Be(RepairError.None);
        attempt.Durability.Should().Be(200f);
        attempt.GoldSpent.Should().Be(17);
        wallet.Gold.Should().Be(983);
        bag.CountItem(Ingot).Should().Be(3);
        bag.GetSlot(SwordSlot).Durability.Should().Be(200f);
    }

    [Fact]
    public void TryRepair_PartialAtAnAnvil_SpendsOneIngotNoGoldAndRestoresTwoSteps()
    {
        (InventoryContainer bag, Wallet wallet) = Kit(swordDurability: 100f, ingots: 1, gold: 1_000);

        RepairAttempt attempt = RepairWorkshop.TryRepair(Table, wallet, bag, SwordSlot, Sword, 2, RepairVenue.Station, Table.ForgeAnvilTier);

        attempt.Error.Should().Be(RepairError.None);
        bag.GetSlot(SwordSlot).Durability.Should().Be(140f);
        bag.CountItem(Ingot).Should().Be(0);
        wallet.Gold.Should().Be(1_000, "a player station charges no fee");
    }

    [Fact]
    public void TryRepair_NotEnoughMaterials_IsRefusedAndNothingIsConsumed()
    {
        (InventoryContainer bag, Wallet wallet) = Kit(swordDurability: 100f, ingots: 1, gold: 1_000);

        RepairAttempt attempt = RepairWorkshop.TryRepair(Table, wallet, bag, SwordSlot, Sword, 5, RepairVenue.Blacksmith, Table.BlacksmithTier);

        attempt.Error.Should().Be(RepairError.MissingMaterials);
        AssertUnchanged(bag, wallet, ingots: 1, gold: 1_000);
    }

    [Fact]
    public void TryRepair_NotEnoughGold_IsRefusedAndNoMaterialIsConsumed()
    {
        (InventoryContainer bag, Wallet wallet) = Kit(swordDurability: 100f, ingots: 5, gold: 16);

        RepairAttempt attempt = RepairWorkshop.TryRepair(Table, wallet, bag, SwordSlot, Sword, 5, RepairVenue.Blacksmith, Table.BlacksmithTier);

        attempt.Error.Should().Be(RepairError.NotEnoughGold);
        AssertUnchanged(bag, wallet, ingots: 5, gold: 16);
    }

    [Fact]
    public void TryRepair_StationBelowTheItemsTier_IsRefused()
    {
        (InventoryContainer bag, Wallet wallet) = Kit(swordDurability: 100f, ingots: 5, gold: 0);

        RepairWorkshop.TryRepair(Table, wallet, bag, SwordSlot, Sword, 5, RepairVenue.Station, Table.WorkbenchTier)
            .Error.Should().Be(RepairError.StationTierTooLow, "iron needs a forge anvil, a workbench is not enough");
        RepairWorkshop.TryRepair(Table, wallet, bag, SwordSlot, Sword, 5, RepairVenue.Station, stationTier: 0)
            .Error.Should().Be(RepairError.OutOfRange);
        AssertUnchanged(bag, wallet, ingots: 5, gold: 0);
    }

    [Fact]
    public void TryRepair_UndamagedItem_IsRefused()
    {
        (InventoryContainer bag, Wallet wallet) = Kit(swordDurability: 200f, ingots: 5, gold: 1_000);

        RepairWorkshop.TryRepair(Table, wallet, bag, SwordSlot, Sword, 1, RepairVenue.Blacksmith, Table.BlacksmithTier)
            .Error.Should().Be(RepairError.NotDamaged);
        bag.CountItem(Ingot).Should().Be(5);
        wallet.Gold.Should().Be(1_000);
    }

    [Fact]
    public void TryRepair_ItemWithoutDurability_IsNotRepairable()
    {
        (InventoryContainer bag, Wallet wallet) = Kit(swordDurability: 100f, ingots: 5, gold: 1_000);

        RepairWorkshop.TryRepair(Table, wallet, bag, IngotSlot, Ingot, 1, RepairVenue.Blacksmith, Table.BlacksmithTier)
            .Error.Should().Be(RepairError.NotRepairable);
        AssertUnchanged(bag, wallet, ingots: 5, gold: 1_000);
    }

    [Fact]
    public void TryRepair_SlotHoldsAnotherItemThanExpected_IsRefused()
    {
        (InventoryContainer bag, Wallet wallet) = Kit(swordDurability: 100f, ingots: 5, gold: 1_000);

        RepairWorkshop.TryRepair(Table, wallet, bag, SwordSlot, Ingot, 5, RepairVenue.Blacksmith, Table.BlacksmithTier)
            .Error.Should().Be(RepairError.ItemMismatch);
        AssertUnchanged(bag, wallet, ingots: 5, gold: 1_000);
    }

    [Fact]
    public void TryRepair_TwoWornSwords_OnlyTheSelectedOneChanges()
    {
        (InventoryContainer bag, Wallet wallet) = Kit(swordDurability: 100f, ingots: 5, gold: 1_000);
        ItemSlot other = new(Sword, 1, InstanceId: 22, Durability: 60f);
        bag.SetSlot(OtherSwordSlot, in other);

        RepairWorkshop.TryRepair(Table, wallet, bag, SwordSlot, Sword, 5, RepairVenue.Blacksmith, Table.BlacksmithTier).Error.Should().Be(RepairError.None);

        bag.GetSlot(SwordSlot).Durability.Should().Be(200f);
        bag.GetSlot(OtherSwordSlot).Should().Be(other, "repair is one item at a time");
    }

    private static (InventoryContainer Bag, Wallet Wallet) Kit(float swordDurability, int ingots, long gold)
    {
        InventoryContainer bag = new(8);
        bag.SetSlot(SwordSlot, new ItemSlot(Sword, 1, InstanceId: 11, Durability: swordDurability));
        if (ingots > 0)
        {
            bag.SetSlot(IngotSlot, new ItemSlot(Ingot, ingots));
        }

        Wallet wallet = new(1);
        if (gold > 0)
        {
            wallet.TryCredit(CurrencyKind.Gold, gold, CurrencyReason.Admin).IsOk.Should().BeTrue();
        }

        return (bag, wallet);
    }

    private static void AssertUnchanged(InventoryContainer bag, Wallet wallet, int ingots, long gold)
    {
        bag.GetSlot(SwordSlot).Durability.Should().Be(100f, "a refused repair changes nothing");
        bag.CountItem(Ingot).Should().Be(ingots, "a refused repair consumes nothing");
        wallet.Gold.Should().Be(gold, "a refused repair charges nothing");
    }
}
