// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Carbon.Economy.Config;
using Carbon.Economy.Loot;
using Carbon.Economy.Serialization;
using Carbon.Economy.Vendor;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Crafting;
using Carbon.Items.Definitions;
using Carbon.Items.Serialization;
using FluentAssertions;
using Xunit;

namespace Carbon.Economy.Tests;

/// <summary>
/// Guards the shipped JSON configs: any edit that breaks references, themes or the price model fails CI.
/// </summary>
public sealed class ConfigIntegrityTests
{
    [Fact]
    public void ShippedConfigs_Load_WithoutValidationErrors()
    {
        ShippedConfig.Items.Count.Should().BeGreaterThan(50);
        ShippedConfig.Recipes.Count.Should().BeGreaterThan(40);
        ShippedConfig.Loot.All.Length.Should().BeGreaterThanOrEqualTo(5);
        ShippedConfig.Economy.MaxLevel.Should().Be(100);
    }

    [Fact]
    public void Recipes_EveryIngredientAndOutput_ReferencesExistingItem()
    {
        RecipeCatalogDocument document = JsonSerializer.Deserialize(ShippedConfig.Read("recipes.json"), ItemsJsonContext.Default.RecipeCatalogDocument)!;

        foreach (RecipeDefinition recipe in document.Recipes)
        {
            foreach (RecipeItemAmount amount in recipe.Ingredients.Concat(recipe.Outputs))
            {
                ShippedConfig.Items.TryGetByCode(amount.Item, out _).Should().BeTrue($"recipe '{recipe.Code}' references '{amount.Item}'");
            }
        }
    }

    [Fact]
    public void LootTables_EveryEntry_ReferencesExistingItem()
    {
        LootTableDocument document = JsonSerializer.Deserialize(ShippedConfig.Read("loot.json"), EconomyJsonContext.Default.LootTableDocument)!;

        foreach (LootTableDefinition table in document.Tables)
        {
            foreach (LootEntryDefinition entry in table.Guaranteed.Concat(table.Entries))
            {
                ShippedConfig.Items.TryGetByCode(entry.Item, out _).Should().BeTrue($"loot table '{table.Id}' references '{entry.Item}'");
            }
        }
    }

    [Theory]
    [InlineData("slime")]
    [InlineData("orc")]
    [InlineData("demon")]
    [InlineData("blood_monster")]
    [InlineData("skeleton")]
    public void LootTables_ExistForEveryMonsterFamily(string monsterId)
    {
        ShippedConfig.Loot.TryGet(monsterId, out LootTable? table).Should().BeTrue($"monster '{monsterId}' needs a loot table");
        table!.Kind.Should().Be(LootSourceKind.Monster);
    }

    [Fact]
    public void LootTables_OnlyContainThemedOrGenericItems()
    {
        foreach (LootTable table in ShippedConfig.Loot.All)
        {
            foreach (LootEntry entry in table.Guaranteed.ToArray().Concat(table.Weighted.ToArray()))
            {
                ItemCatalogEntry item = Item(entry.ItemId);
                item.Tags.Should().Contain(
                    tag => tag == table.Theme || tag == LootCatalog.GenericTag,
                    $"'{table.Id}' may only drop '{table.Theme}'-themed or generic items, but lists '{item.Code}'");
            }
        }
    }

    [Fact]
    public void ItemsJson_SharedIds_MatchStarterGameCatalog()
    {
        foreach (ItemArchetype archetype in StarterGameCatalog.Items.All)
        {
            ShippedConfig.Items.TryGet(archetype.Id, out ItemCatalogEntry? item).Should().BeTrue($"items.json must define starter item {archetype.Id}");
            item!.Code.Should().Be(archetype.Code);
            item.Category.Should().Be(archetype.Category, $"'{archetype.Code}' category");
            item.MaxStack.Should().Be(archetype.MaxStackSize, $"'{archetype.Code}' stack size");
            item.MaxDurability.Should().Be(archetype.MaxDurability, $"'{archetype.Code}' durability");
        }
    }

    [Fact]
    public void Food_EveryFoodItem_HasNutrition()
    {
        foreach (ItemCatalogEntry item in ShippedConfig.Items.All)
        {
            if (item.Tags.Contains("food"))
            {
                item.Category.Should().Be(ItemCategory.Consumable, $"'{item.Code}' is food");
                item.Nutrition.Should().BePositive($"'{item.Code}' is food");
            }
        }
    }

    [Fact]
    public void Items_ThatWearOut_HavePriceForRepair()
    {
        foreach (ItemCatalogEntry item in ShippedConfig.Items.All)
        {
            if (item.MaxDurability > 0f)
            {
                item.BasePrice.Should().BePositive($"'{item.Code}' needs a base price so repair is a real sink");
            }
        }
    }

    [Fact]
    public void Recipes_NeverDestroyBaseValue()
    {
        PriceBook prices = new(ShippedConfig.Items, ShippedConfig.Economy);
        foreach (CraftingRecipe recipe in ShippedConfig.Recipes.All)
        {
            prices.GetOutputValue(recipe).Should().BeGreaterThanOrEqualTo(
                prices.GetIngredientValue(recipe),
                $"crafting '{recipe.Code}' should add value at base prices");
        }
    }

    [Fact]
    public void Vendor_BuyCraftSellLoops_NeverMintGold()
    {
        PriceBook prices = new(ShippedConfig.Items, ShippedConfig.Economy);
        foreach (ItemCatalogEntry item in ShippedConfig.Items.All)
        {
            long buy = prices.GetBuyPrice(item.Id);
            if (buy >= 0)
            {
                prices.GetSellPrice(item.Id).Should().BeLessThan(Math.Max(1, buy), $"'{item.Code}' sells for less than it costs");
            }
        }

        foreach (CraftingRecipe recipe in ShippedConfig.Recipes.All)
        {
            long cost = prices.GetCraftingFee(recipe);
            bool allBuyable = true;
            foreach (RecipeIngredient ingredient in recipe.Ingredients)
            {
                long unit = prices.GetBuyPrice(ingredient.ItemId);
                allBuyable &= unit >= 0;
                cost += Math.Max(0, unit) * ingredient.Quantity;
            }

            if (!allBuyable)
            {
                continue;
            }

            long resale = 0;
            for (int i = 0; i < recipe.OutputCount; i++)
            {
                ItemSlot output = recipe.GetOutput(i);
                resale += prices.GetSellPrice(output.ItemId) * output.Quantity;
            }

            resale.Should().BeLessThan(Math.Max(1, cost), $"buying ingredients for '{recipe.Code}' and selling the result must lose gold");
        }
    }

    [Fact]
    public void ItemCatalog_WithUnknownProperty_FailsLoudly()
    {
        const string json = """{ "version": 1, "items": [ { "id": 1, "code": "x", "basePrise": 5 } ] }""";
        Action act = () => ItemCatalog.LoadFromJson(json);
        act.Should().Throw<InvalidDataException>().WithMessage("*basePrise*");
    }

    [Fact]
    public void Loaders_WithOmittedOptionalFields_KeepDocumentedDefaults()
    {
        ItemCatalog items = ItemCatalog.LoadFromJson("""{ "version": 1, "items": [ { "id": 1, "code": "x" } ] }""");
        items.TryGet(1, out ItemCatalogEntry? item).Should().BeTrue();
        item!.MaxStack.Should().Be(99);
        item.Tags.Should().BeEmpty();

        LootCatalog loot = LootCatalog.LoadFromJson(
            """{ "version": 1, "tables": [ { "id": "slime", "minLevel": 1, "maxLevel": 5, "entries": [ { "item": "slime_gel" } ] } ] }""",
            ShippedConfig.Items,
            ShippedConfig.Economy);
        loot.TryGet("slime", out LootTable? table).Should().BeTrue();
        table!.Rolls.Should().Be(1);
        table.EliteExtraRolls.Should().Be(1);
        table.BossExtraRolls.Should().Be(3);
        table.Weighted[0].Weight.Should().Be(1);
    }

    [Fact]
    public void RecipeCatalog_WithUnknownItem_FailsLoudly()
    {
        const string json = """
            { "version": 1, "recipes": [ { "id": 1, "code": "r", "ingredients": [ { "item": "wood_log", "qty": 1 } ], "outputs": [ { "item": "unobtainium", "qty": 1 } ] } ] }
            """;
        Action act = () => RecipeCatalog.LoadFromJson(json, ShippedConfig.Items);
        act.Should().Throw<InvalidDataException>().WithMessage("*unobtainium*");
    }

    [Fact]
    public void LootCatalog_WithUnknownItem_FailsLoudly()
    {
        const string json = """
            { "version": 1, "tables": [ { "id": "slime", "minLevel": 1, "maxLevel": 5, "entries": [ { "item": "slime_gell", "weight": 1 } ] } ] }
            """;
        Action act = () => LootCatalog.LoadFromJson(json, ShippedConfig.Items, ShippedConfig.Economy);
        act.Should().Throw<InvalidDataException>().WithMessage("*slime_gell*");
    }

    [Fact]
    public void EconomyConfig_WithSellAboveBuy_FailsValidation()
    {
        EconomyConfig config = ShippedConfig.Economy with { Vendor = new VendorRules { BuyMarkupPercent = 100, SellRatioPercent = 100 } };
        Action act = config.Validate;
        act.Should().Throw<InvalidDataException>().WithMessage("*sellRatioPercent*");
    }

    [Fact]
    public void DesignTargets_CoverEveryLevel()
    {
        DesignTargets targets = ShippedConfig.Economy.DesignTargets;
        for (int level = 1; level <= ShippedConfig.Economy.MaxLevel; level++)
        {
            targets.GoldPerHour.Should().ContainSingle(b => b.MinLevel <= level && level <= b.MaxLevel, $"gold band for level {level}");
            targets.DiamondsPerHour.Should().ContainSingle(b => b.MinLevel <= level && level <= b.MaxLevel, $"diamond band for level {level}");
        }
    }

    private static ItemCatalogEntry Item(uint id) =>
        ShippedConfig.Items.TryGet(id, out ItemCatalogEntry? item) ? item : throw new KeyNotFoundException(id.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
