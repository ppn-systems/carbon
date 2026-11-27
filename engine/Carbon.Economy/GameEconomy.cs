// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Carbon.Configuration.Catalog;
using Carbon.Economy.Config;
using Carbon.Economy.Crafting;
using Carbon.Economy.Loot;
using Carbon.Economy.Vendor;
using Carbon.Items.Catalog;
using Carbon.Items.Crafting;
using Carbon.Items.Enhancement;

namespace Carbon.Economy;

/// <summary>
/// The validated economy data set the server loads at boot from <c>resources/config</c>:
/// items, recipes, tunables and loot tables plus the services built on them.
/// </summary>
public sealed class GameEconomy
{
    /// <summary>File name of the item catalog.</summary>
    public const string ItemsFile = "items.json";

    /// <summary>File name of the recipe catalog.</summary>
    public const string RecipesFile = "recipes.json";

    /// <summary>File name of the economy tunables.</summary>
    public const string EconomyFile = "economy.json";

    /// <summary>File name of the loot tables.</summary>
    public const string LootFile = "loot.json";

    /// <summary>File name of the forge enhancement table (ADR-020).</summary>
    public const string EnhancementFile = "enhancement.json";

    private GameEconomy(ItemCatalog items, RecipeCatalog recipes, EconomyConfig config, LootCatalog loot, EnhancementTable enhancement)
    {
        this.Items = items;
        this.Recipes = recipes;
        this.Config = config;
        this.Loot = loot;
        this.ItemRules = new ItemRules(items, recipes, enhancement);
        this.Prices = new PriceBook(items, config);
        this.Vendor = new VendorService(this.Prices);
        this.Checkout = new CraftingCheckout(this.Prices);
    }

    /// <summary>Gets the item catalog.</summary>
    public ItemCatalog Items { get; }

    /// <summary>Gets the recipe catalog.</summary>
    public RecipeCatalog Recipes { get; }

    /// <summary>Gets the item stat and recipe rules built from <see cref="Items"/> and <see cref="Recipes"/>.</summary>
    public ItemRules ItemRules { get; }

    /// <summary>Gets the economy tunables.</summary>
    public EconomyConfig Config { get; }

    /// <summary>Gets the loot tables.</summary>
    public LootCatalog Loot { get; }

    /// <summary>Gets the price rules.</summary>
    public PriceBook Prices { get; }

    /// <summary>Gets the vendor service.</summary>
    public VendorService Vendor { get; }

    /// <summary>Gets the crafting checkout (station fees).</summary>
    public CraftingCheckout Checkout { get; }

    /// <summary>
    /// Builds the economy from the four JSON documents, with the build-time copy of the shipped <c>enhancement.json</c>
    /// (tests and offline tools; the server uses <see cref="Load(ConfigCatalog)"/>).
    /// </summary>
    /// <param name="itemsJson"><c>items.json</c> text.</param>
    /// <param name="recipesJson"><c>recipes.json</c> text.</param>
    /// <param name="economyJson"><c>economy.json</c> text.</param>
    /// <param name="lootJson"><c>loot.json</c> text.</param>
    /// <returns>The validated economy.</returns>
    /// <exception cref="InvalidDataException">Thrown when any document is malformed or inconsistent.</exception>
    public static GameEconomy Load(string itemsJson, string recipesJson, string economyJson, string lootJson) =>
        Load(itemsJson, recipesJson, economyJson, lootJson, ItemRules.ReadShippedDocument(EnhancementFile));

    /// <summary>
    /// Builds the economy from the five JSON documents.
    /// </summary>
    /// <param name="itemsJson"><c>items.json</c> text.</param>
    /// <param name="recipesJson"><c>recipes.json</c> text.</param>
    /// <param name="economyJson"><c>economy.json</c> text.</param>
    /// <param name="lootJson"><c>loot.json</c> text.</param>
    /// <param name="enhancementJson"><c>enhancement.json</c> text.</param>
    /// <returns>The validated economy.</returns>
    /// <exception cref="InvalidDataException">Thrown when any document is malformed or inconsistent.</exception>
    public static GameEconomy Load(string itemsJson, string recipesJson, string economyJson, string lootJson, string enhancementJson)
    {
        ItemCatalog items = ItemCatalog.LoadFromJson(itemsJson);
        RecipeCatalog recipes = RecipeCatalog.LoadFromJson(recipesJson, items);
        EconomyConfig config = EconomyConfig.LoadFromJson(economyJson);
        LootCatalog loot = LootCatalog.LoadFromJson(lootJson, items, config);
        EnhancementTable enhancement = EnhancementTable.LoadFromJson(enhancementJson, items);
        return new GameEconomy(items, recipes, config, loot, enhancement);
    }

    /// <summary>
    /// Builds the economy from the server's config snapshot (<c>ConfigRepository.Catalog</c>), so the server simulates
    /// exactly the bytes it serves to clients.
    /// </summary>
    /// <param name="config">Config snapshot holding <c>items</c>, <c>recipes</c>, <c>economy</c>, <c>loot</c> and <c>enhancement</c>.</param>
    /// <returns>The validated economy.</returns>
    /// <exception cref="InvalidDataException">Thrown when a document is malformed or inconsistent.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when a document is missing.</exception>
    public static GameEconomy Load(ConfigCatalog config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return Load(
            Read(config, ItemsFile),
            Read(config, RecipesFile),
            Read(config, EconomyFile),
            Read(config, LootFile),
            Read(config, EnhancementFile));
    }

    /// <summary>
    /// Loads the economy from a directory containing the four config files.
    /// </summary>
    /// <param name="configDirectory">Directory holding <c>items.json</c>, <c>recipes.json</c>, <c>economy.json</c>, <c>loot.json</c> and <c>enhancement.json</c>.</param>
    /// <returns>The validated economy.</returns>
    /// <exception cref="InvalidDataException">Thrown when any document is malformed or inconsistent.</exception>
    /// <exception cref="FileNotFoundException">Thrown when a file is missing.</exception>
    public static GameEconomy LoadFromDirectory(string configDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configDirectory);
        return Load(
            File.ReadAllText(Path.Combine(configDirectory, ItemsFile)),
            File.ReadAllText(Path.Combine(configDirectory, RecipesFile)),
            File.ReadAllText(Path.Combine(configDirectory, EconomyFile)),
            File.ReadAllText(Path.Combine(configDirectory, LootFile)),
            File.ReadAllText(Path.Combine(configDirectory, EnhancementFile)));
    }

    /// <summary>
    /// Searches <paramref name="startDirectory"/> and its ancestors for <c>resources/config</c> (or a <c>config</c>
    /// folder copied next to the binaries) that contains <c>economy.json</c>.
    /// </summary>
    /// <param name="startDirectory">Directory to start from (usually <see cref="AppContext.BaseDirectory"/>).</param>
    /// <returns>The config directory, or <see langword="null"/> when none was found.</returns>
    public static string? FindConfigDirectory(string startDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(startDirectory);
        for (DirectoryInfo? dir = new(startDirectory); dir is not null; dir = dir.Parent)
        {
            string copied = Path.Combine(dir.FullName, "config");
            if (File.Exists(Path.Combine(copied, EconomyFile)))
            {
                return copied;
            }

            string repo = Path.Combine(dir.FullName, "resources", "config");
            if (File.Exists(Path.Combine(repo, EconomyFile)))
            {
                return repo;
            }
        }

        return null;
    }

    private static string Read(ConfigCatalog config, string fileName) =>
        Encoding.UTF8.GetString(config.GetUtf8(Path.GetFileNameWithoutExtension(fileName)));
}
