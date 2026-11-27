// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using Carbon.Contracts.Common;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Crafting;
using Carbon.Items.Repair;
using FluentAssertions;
using Xunit;

namespace Carbon.Items.Tests;

/// <summary>
/// The shipped <c>repair.json</c> (ADR-028): material families by item material and tier, step quotes, the blacksmith
/// fee and validation. Expected values are worked out by hand from the shipped numbers, not with the formula.
/// </summary>
public sealed class RepairTableTests
{
    private static readonly ItemRules s_rules = ItemRules.LoadShippedDefaults();
    private static readonly string s_shippedJson = ItemRules.ReadShippedDocument("repair.json");

    private static RepairTable Table => s_rules.Repair;

    private static uint Id(string code) => s_rules.ItemCatalog.ResolveId(code);

    private static ItemSlot Item(string code, float durability, byte level = 0) => new(Id(code), 1, InstanceId: 7, Durability: durability, UpgradeLevel: level);

    [Theory]
    [InlineData("wood_axe", "wood_log", "plant_fiber")]
    [InlineData("stone_pickaxe", "stone_chunk", "plant_fiber")]
    [InlineData("iron_sword", "iron_ingot", null)]
    [InlineData("iron_chestplate", "iron_ingot", null)]
    [InlineData("leather_tunic", "leather", null)]
    [InlineData("crystal_pickaxe", "mana_crystal", null)]
    public void TryQuoteFull_ShippedFamilies_UseTheItemsOwnMaterial(string item, string first, string? second)
    {
        Table.TryQuoteFull(Item(item, 0f), RepairVenue.Station, out RepairQuote quote).Should().Be(RepairError.None);

        quote.MaterialCount.Should().Be(second is null ? 1 : 2);
        quote.GetMaterial(0).ItemId.Should().Be(Id(first));
        if (second is not null)
        {
            quote.GetMaterial(1).ItemId.Should().Be(Id(second));
        }
    }

    [Fact]
    public void TryQuote_HalfWornIronSwordAtTheBlacksmith_FullAndPartialPinnedValues()
    {
        // Iron sword: 200 max, tier 2 (150 %), uncommon, 110 base gold; iron family = 2 ingots per full repair.
        ItemSlot sword = Item("iron_sword", 100f);

        Table.TryQuote(sword, 5, RepairVenue.Blacksmith, out RepairQuote full).Should().Be(RepairError.None);
        full.StepsNeeded.Should().Be(5, "100 of 200 missing in 10 % steps");
        full.IsFull.Should().BeTrue();
        full.NewDurability.Should().Be(200f);
        full.GetMaterial(0).Should().Be(new RecipeIngredient(Id("iron_ingot"), 2));
        full.GoldFee.Should().Be(17);
        full.RequiredStationTier.Should().Be(2);

        Table.TryQuote(sword, 2, RepairVenue.Blacksmith, out RepairQuote partial).Should().Be(RepairError.None);
        partial.IsFull.Should().BeFalse();
        partial.RestoredDurability.Should().Be(40f);
        partial.NewDurability.Should().Be(140f);
        partial.GetMaterial(0).Quantity.Should().Be(1);
        partial.GoldFee.Should().Be(7);
    }

    [Fact]
    public void TryQuote_AtAPlayerStation_ChargesNoGold()
    {
        Table.TryQuote(Item("iron_sword", 100f), 5, RepairVenue.Station, out RepairQuote quote).Should().Be(RepairError.None);

        quote.GoldFee.Should().Be(0);
        quote.GetMaterial(0).Quantity.Should().Be(2, "materials do not depend on the venue");
    }

    [Fact]
    public void TryQuoteFull_BrokenWoodAxe_TakesTwoLogsAndTwoFibres()
    {
        Table.TryQuoteFull(Item("wood_axe", 0f), RepairVenue.Blacksmith, out RepairQuote quote).Should().Be(RepairError.None);

        quote.StepsNeeded.Should().Be(10);
        quote.GetMaterial(0).Should().Be(new RecipeIngredient(Id("wood_log"), 2));
        quote.GetMaterial(1).Should().Be(new RecipeIngredient(Id("plant_fiber"), 2));
        quote.GoldFee.Should().Be(2, "common items pay half the fee: 30 % of 12 gold, halved, rounded up");
        quote.RequiredStationTier.Should().Be(1, "a workbench is enough for wood");
    }

    [Fact]
    public void TryQuoteFull_HigherTierCrystalPick_ScalesMaterialsAndNeedsTheBlacksmith()
    {
        // Crystal pickaxe: 250 max, tier 3 (200 %), rare (175 %), 170 base; half worn = 5 steps.
        Table.TryQuoteFull(Item("crystal_pickaxe", 125f), RepairVenue.Blacksmith, out RepairQuote quote).Should().Be(RepairError.None);

        quote.StepsNeeded.Should().Be(5);
        quote.GetMaterial(0).Should().Be(new RecipeIngredient(Id("mana_crystal"), 2));
        quote.GoldFee.Should().Be(45);
        quote.RequiredStationTier.Should().Be(Table.BlacksmithTier);
    }

    [Fact]
    public void TryQuoteFull_EnhancedSword_RepairsToTheEnhancedMaximum()
    {
        // +5 iron sword: 200 × 1.5 = 300 max; at 200 it is a third worn (4 steps).
        Table.TryQuoteFull(Item("iron_sword", 200f, level: 5), RepairVenue.Station, out RepairQuote quote).Should().Be(RepairError.None);

        quote.MaxDurability.Should().Be(300f);
        quote.StepsNeeded.Should().Be(4);
        quote.NewDurability.Should().Be(300f);
    }

    [Fact]
    public void TryQuote_InvalidRequests_AreRefusedWithTheirReason()
    {
        Table.TryQuote(Item("iron_sword", 200f), 1, RepairVenue.Station, out _).Should().Be(RepairError.NotDamaged);
        Table.TryQuote(new ItemSlot(Id("wood_log"), 5), 1, RepairVenue.Station, out _).Should().Be(RepairError.NotRepairable);
        Table.TryQuote(ItemSlot.Empty, 1, RepairVenue.Station, out _).Should().Be(RepairError.EmptySlot);
        Table.TryQuote(Item("iron_sword", 100f), 6, RepairVenue.Station, out _).Should().Be(RepairError.TooManySteps);
        Table.TryQuote(Item("iron_sword", 100f), 0, RepairVenue.Station, out _).Should().Be(RepairError.InvalidRequest);
        Table.TryQuote(Item("iron_sword", 100f), 1, RepairVenue.None, out _).Should().Be(RepairError.InvalidRequest);
    }

    [Fact]
    public void GetAffordableSteps_ShortOfIngots_IsTheLargestStepCountTheBagPaysFor()
    {
        ItemSlot[] bag = [new ItemSlot(Id("iron_ingot"), 1), ItemSlot.Empty];

        Table.GetAffordableSteps(Item("iron_sword", 0f), RepairVenue.Station, bag, gold: 0)
            .Should().Be(3, "one ingot covers up to 3 steps (3 × 10 % × 2 × 150 % = 0.9 ingots)");
    }

    [Fact]
    public void GetAffordableSteps_AtTheBlacksmithShortOfGold_StopsAtTheFeeThePlayerCanPay()
    {
        // Enough ingots for a full repair of a half-worn sword (5 steps, 17 g), but only 7 gold: 2 steps cost 7 g.
        ItemSlot[] bag = [new ItemSlot(Id("iron_ingot"), 9)];

        Table.GetAffordableSteps(Item("iron_sword", 100f), RepairVenue.Blacksmith, bag, gold: 7).Should().Be(2);
        Table.GetAffordableSteps(Item("iron_sword", 100f), RepairVenue.Station, bag, gold: 0).Should().Be(5, "a station charges no fee");
    }

    [Fact]
    public void LoadShippedDefaults_EveryItemWithDurability_HasAFamily()
    {
        foreach (Definitions.ItemCatalogEntry item in s_rules.ItemCatalog.All)
        {
            Table.TryGetFamily(item.Id, out RepairFamily? family).Should().Be(item.MaxDurability > 0f, item.Code);
            if (item.MaxDurability > 0f)
            {
                family!.Materials.Length.Should().BePositive(item.Code);
            }
        }
    }

    [Fact]
    public void LoadShippedDefaults_ItemStatingAMaterial_IsRepairedWithThatMaterialFamily()
    {
        int stated = 0;
        foreach (Definitions.ItemCatalogEntry item in s_rules.ItemCatalog.All)
        {
            if (item.Material == Definitions.ItemMaterial.None)
            {
                continue;
            }

            stated++;
            Table.TryGetFamily(item.Id, out RepairFamily? family).Should().BeTrue(item.Code);
            family!.Id.Should().Be(item.Material.ToString().ToLowerInvariant(), $"items.json says '{item.Code}' is {item.Material}");
        }

        stated.Should().BePositive("the wood and stone tools state their material");
    }

    [Theory]
    [InlineData("\"item\": \"iron_ingot\", \"qty\": 2 } ],\n      \"items\": [ \"iron_sword\"", "\"item\": \"no_such_item\", \"qty\": 2 } ],\n      \"items\": [ \"iron_sword\"", "materials must exist in items.json")]
    [InlineData(", \"orc_war_helm\" ]", " ]", "every item with durability needs a family")]
    [InlineData("\"items\": [ \"bone_bow\" ]", "\"items\": [ \"bone_bow\", \"iron_sword\" ]", "an item belongs to one family")]
    [InlineData("\"stepPercent\": 10", "\"stepPercent\": 7", "steps must divide a full repair")]
    [InlineData("\"stationReachTiles\": 5", "\"stationReachTiles\": 0", "a station must be reachable")]
    [InlineData("{ \"item\": \"leather\", \"qty\": 2 }", "{ \"item\": \"iron_sword\", \"qty\": 2 }", "a material cannot itself have durability")]
    [InlineData("\"stationTier\": 3,\n      \"materials\": [ { \"item\": \"mana_crystal\"", "\"stationTier\": 4,\n      \"materials\": [ { \"item\": \"mana_crystal\"", "a family needs a tier some station provides")]
    public void LoadFromJson_InvalidDocument_IsRejected(string find, string replace, string because)
    {
        ArgumentNullException.ThrowIfNull(find);
        s_shippedJson.Should().Contain(find);
        int at = s_shippedJson.IndexOf(find, StringComparison.Ordinal);
        string broken = string.Concat(s_shippedJson.AsSpan(0, at), replace, s_shippedJson.AsSpan(at + find.Length));

        Action load = () => RepairTable.LoadFromJson(broken, s_rules.ItemCatalog);

        load.Should().Throw<InvalidDataException>(because);
    }
}
