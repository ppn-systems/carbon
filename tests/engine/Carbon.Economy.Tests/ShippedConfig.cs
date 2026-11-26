// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using Carbon.Economy.Config;
using Carbon.Economy.Loot;
using Carbon.Items.Catalog;
using Carbon.Items.Crafting;

namespace Carbon.Economy.Tests;

/// <summary>
/// Loads the shipped <c>resources/config</c> JSON files once per test run.
/// </summary>
internal static class ShippedConfig
{
    private static readonly Lazy<ItemCatalog> s_items = new(() => ItemCatalog.LoadFromJson(Read("items.json")));
    private static readonly Lazy<RecipeCatalog> s_recipes = new(() => RecipeCatalog.LoadFromJson(Read("recipes.json"), Items));
    private static readonly Lazy<EconomyConfig> s_economy = new(() => EconomyConfig.LoadFromJson(Read("economy.json")));
    private static readonly Lazy<LootCatalog> s_loot = new(() => LootCatalog.LoadFromJson(Read("loot.json"), Items, Economy));

    public static ItemCatalog Items => s_items.Value;

    public static RecipeCatalog Recipes => s_recipes.Value;

    public static EconomyConfig Economy => s_economy.Value;

    public static LootCatalog Loot => s_loot.Value;

    public static string Read(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "config", fileName));
}
