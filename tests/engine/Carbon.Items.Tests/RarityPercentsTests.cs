// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using Carbon.Contracts.Common;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Items.Enhancement;
using Carbon.Items.Repair;
using FluentAssertions;
using Xunit;

namespace Carbon.Items.Tests;

/// <summary>
/// Per-rarity fee percents in <c>enhancement.json</c> (<c>goldRarityPercent</c>) and <c>repair.json</c>
/// (<c>blacksmithFee.rarityPercent</c>): which rarity reads which value, the 100 % default for a missing block or an
/// unknown rarity, and the rejection of negative values. Pinned against the tables before both moved onto the shared
/// <c>RarityPercents</c> record (audit round 3, DUP-14). Fees are worked out by hand from the shipped numbers.
/// </summary>
public sealed class RarityPercentsTests
{
    private static readonly ItemRules s_rules = ItemRules.LoadShippedDefaults();
    private static readonly string s_enhancementJson = ItemRules.ReadShippedDocument("enhancement.json");
    private static readonly string s_repairJson = ItemRules.ReadShippedDocument("repair.json");

    [Theory]
    [InlineData(ItemRarity.Common, 20L)] // level 0 costs 20 gold; common 100 %
    [InlineData(ItemRarity.Uncommon, 30L)] // 150 %
    [InlineData(ItemRarity.Rare, 50L)] // 250 %
    [InlineData(ItemRarity.Epic, 80L)] // 400 %
    [InlineData(ItemRarity.Legendary, 140L)] // 700 %
    [InlineData((ItemRarity)99, 20L)] // unknown rarity: 100 %
    public void GetGoldFee_ShippedTableLevelZero_ScalesByTheRaritysOwnPercent(ItemRarity rarity, long expected)
    {
        s_rules.Enhancement.TryGetStep(0, out EnhancementStep? step).Should().BeTrue();

        s_rules.Enhancement.GetGoldFee(step!, rarity).Should().Be(expected);
    }

    [Fact]
    public void GetGoldFee_NoGoldRarityPercentBlock_ChargesTheBaseGoldForEveryRarity()
    {
        string json = Replace(s_enhancementJson, "\"goldRarityPercent\": {\n    \"common\": 100,\n    \"uncommon\": 150,\n    \"rare\": 250,\n    \"epic\": 400,\n    \"legendary\": 700\n  },\n", string.Empty);
        EnhancementTable table = EnhancementTable.LoadFromJson(json, s_rules.ItemCatalog);
        table.TryGetStep(0, out EnhancementStep? step).Should().BeTrue();

        table.GetGoldFee(step!, ItemRarity.Legendary).Should().Be(20L, "a missing block means 100 % for every rarity");
    }

    [Fact]
    public void LoadFromJson_NegativeGoldRarityPercent_IsRejectedWithTheFieldName()
    {
        string json = Replace(s_enhancementJson, "\"epic\": 400", "\"epic\": -1");

        FluentActions.Invoking(() => EnhancementTable.LoadFromJson(json, s_rules.ItemCatalog))
            .Should().Throw<InvalidDataException>().WithMessage("enhancement.json goldRarityPercent values must not be negative.");
    }

    [Theory]
    [InlineData("\"uncommon\": 100", "\"uncommon\": 200", "iron_sword", 33L)] // iron sword is uncommon: its fee follows uncommon
    [InlineData("\"common\": 50", "\"common\": 999", "iron_sword", 17L)] // ... and ignores common
    [InlineData("\"common\": 50", "\"common\": 100", "wood_axe", 2L)] // wood axe is common: its fee follows common
    [InlineData("\"uncommon\": 100", "\"uncommon\": 999", "wood_axe", 1L)] // ... and ignores uncommon
    public void TryQuote_BlacksmithFee_UsesTheItemsRarityPercent(string find, string replace, string code, long expectedFee)
    {
        ArgumentNullException.ThrowIfNull(find);
        ArgumentNullException.ThrowIfNull(replace);
        ArgumentNullException.ThrowIfNull(code);
        RepairTable table = RepairTable.LoadFromJson(Replace(s_repairJson, find, replace), s_rules.ItemCatalog);

        Quote(table, code).GoldFee.Should().Be(expectedFee);
    }

    [Fact]
    public void TryQuote_ShippedBlacksmithFee_PinsCommonAndUncommon()
    {
        Quote(s_rules.Repair, "iron_sword").GoldFee.Should().Be(17L);
        Quote(s_rules.Repair, "wood_axe").GoldFee.Should().Be(1L);
    }

    [Fact]
    public void LoadFromJson_NegativeRepairRarityPercent_IsRejected()
    {
        string json = Replace(s_repairJson, "\"legendary\": 400", "\"legendary\": -5");

        FluentActions.Invoking(() => RepairTable.LoadFromJson(json, s_rules.ItemCatalog))
            .Should().Throw<InvalidDataException>().WithMessage("repair.json blacksmithFee values must not be negative.");
    }

    private static RepairQuote Quote(RepairTable table, string code)
    {
        s_rules.ItemCatalog.TryGetByCode(code, out ItemCatalogEntry? item).Should().BeTrue(code);
        ItemSlot halfWorn = new(item!.Id, 1, InstanceId: 7, Durability: item.MaxDurability / 2f);
        table.TryQuote(halfWorn, 5, RepairVenue.Blacksmith, out RepairQuote quote).Should().Be(RepairError.None);
        return quote;
    }

    private static string Replace(string json, string find, string replace)
    {
        json.Should().Contain(find);
        int at = json.IndexOf(find, StringComparison.Ordinal);
        return string.Concat(json.AsSpan(0, at), replace, json.AsSpan(at + find.Length));
    }
}
