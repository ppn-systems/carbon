// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Carbon.Contracts.Common;
using Carbon.Configuration;
using Carbon.Configuration.Catalog;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Items.Repair;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>The desktop reads item names, stats and recipes from the server's synced <c>items.json</c> and <c>recipes.json</c>.</summary>
public sealed class SyncedItemCatalogTests
{
    private static readonly byte[] s_itemsJson = File.ReadAllBytes(Path.Combine(FindRepositoryRoot(), "resources", "config", "items.json"));
    private static readonly byte[] s_recipesJson = File.ReadAllBytes(Path.Combine(FindRepositoryRoot(), "resources", "config", "recipes.json"));
    private static readonly byte[] s_enhancementJson = File.ReadAllBytes(Path.Combine(FindRepositoryRoot(), "resources", "config", "enhancement.json"));
    private static readonly byte[] s_repairJson = File.ReadAllBytes(Path.Combine(FindRepositoryRoot(), "resources", "config", "repair.json"));

    [Fact]
    public void Apply_SyncedItemsDocument_ServesNamesAndStacksForItemsTheClientNeverCompiled()
    {
        SyncedItemCatalog synced = new();

        synced.Apply(Config(1, s_itemsJson)).Should().BeTrue();

        ItemCatalog catalog = synced.Current!;
        ItemCatalogEntry beyondStarter = catalog.All.ToArray().First(static i => i.Id >= 100);
        synced.GetDisplayName(beyondStarter.Id).Should().Be(beyondStarter.Name);
        synced.GetMaxStackSize(beyondStarter.Id).Should().Be(beyondStarter.MaxStack);
        synced.GetDisplayName(0U).Should().Be("Empty");
        synced.GetDisplayName(uint.MaxValue).Should().Be($"Item #{uint.MaxValue}");
        synced.Rules.Items.Contains(beyondStarter.Id).Should().BeTrue("the synced catalog backs the session's item rules");
        synced.Rules.Recipes.Count.Should().Be(synced.Rules.RecipeCatalog.Count);
    }

    [Fact]
    public void Apply_MissingOrInvalidItemsDocument_KeepsPreviousCatalog()
    {
        SyncedItemCatalog synced = new();
        synced.Apply(Config(1, s_itemsJson)).Should().BeTrue();
        ItemCatalog? before = synced.Current;
        ItemRules rulesBefore = synced.Rules;

        synced.Apply(new ConfigCatalog(2, [])).Should().BeFalse();
        synced.Apply(new ConfigCatalog(3, [new ConfigCatalogEntry("items", 0, 1, "{\"items\":[{\"id\":0}]}"u8.ToArray())])).Should().BeFalse();
        synced.Apply(new ConfigCatalog(4, [new ConfigCatalogEntry("items", 0, 1, s_itemsJson)])).Should().BeFalse("recipes.json must sync with items.json so recipe ids match the server");

        synced.Current.Should().BeSameAs(before);
        synced.Rules.Should().BeSameAs(rulesBefore);
    }

    [Fact]
    public void Apply_RecipesDocumentWithWorkTicks_IsRejectedAndKeepsPreviousCatalog()
    {
        SyncedItemCatalog synced = new();
        synced.Apply(Config(1, s_itemsJson)).Should().BeTrue();
        ItemRules rulesBefore = synced.Rules;
        string recipes = System.Text.Encoding.UTF8.GetString(s_recipesJson)
            .Replace("\"code\": \"wood_plank\",", "\"code\": \"wood_plank\", \"workTicks\": 30,", StringComparison.Ordinal);

        synced.Apply(Config(2, s_itemsJson, System.Text.Encoding.UTF8.GetBytes(recipes))).Should().BeFalse("recipes carry no work amount any more");

        synced.Rules.Should().BeSameAs(rulesBefore);
    }

    [Fact]
    public void Apply_ServerChangedCombatStatsAndRecipes_DesktopUsesSyncedValuesNotCompiledOnes()
    {
        // The server edits items.json/recipes.json; the client must show and predict with the synced values.
        string items = System.Text.Encoding.UTF8.GetString(s_itemsJson)
            .Replace("\"baseDamage\": 34.0", "\"baseDamage\": 999.0", StringComparison.Ordinal);
        string recipes = System.Text.Encoding.UTF8.GetString(s_recipesJson)
            .Replace("\"code\": \"wood_plank\"", "\"code\": \"wood_plank_synced\"", StringComparison.Ordinal);
        SyncedItemCatalog synced = new();

        synced.Apply(Config(5, System.Text.Encoding.UTF8.GetBytes(items), System.Text.Encoding.UTF8.GetBytes(recipes))).Should().BeTrue();

        synced.Rules.Items.GetRequired(StarterGameCatalog.IronSword).BaseDamage.Should().Be(999f, "iron sword damage comes from the synced items.json");
        synced.Rules.GetWeaponBaseDamage(StarterGameCatalog.IronSword).Should().Be(999f);
        synced.Rules.RecipeCatalog.TryGetByCode("wood_plank_synced", out _).Should().BeTrue("recipes come from the synced recipes.json");
    }

    [Fact]
    public void Apply_TwoSessions_KeepIndependentTables()
    {
        // No process-wide installed table: a second session (or a test) syncing other values never leaks into the first.
        string items = System.Text.Encoding.UTF8.GetString(s_itemsJson)
            .Replace("\"baseDamage\": 34.0", "\"baseDamage\": 999.0", StringComparison.Ordinal);
        SyncedItemCatalog first = new();
        SyncedItemCatalog second = new();

        first.Apply(Config(1, s_itemsJson)).Should().BeTrue();
        second.Apply(Config(2, System.Text.Encoding.UTF8.GetBytes(items))).Should().BeTrue();

        first.Rules.GetWeaponBaseDamage(StarterGameCatalog.IronSword).Should().Be(34f);
        second.Rules.GetWeaponBaseDamage(StarterGameCatalog.IronSword).Should().Be(999f);
    }

    [Fact]
    public void ClientAndEngineSources_NeverConstructItemOrRecipeDefinitionsFromHardcodedTables()
    {
        string root = FindRepositoryRoot();
        string[] forbidden = ["new ItemArchetype(", "new CraftingRecipe(", "new RecipeIngredient(", "new ItemCatalogEntry", "new RecipeDefinition"];
        IEnumerable<string> sources = Directory.EnumerateFiles(Path.Combine(root, "desktop"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "graphics"), "*.cs", SearchOption.AllDirectories))
            .Append(Path.Combine(root, "engine", "Carbon.Items", "Catalog", "StarterGameCatalog.cs"))
            .Where(static f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        foreach (string file in sources)
        {
            string text = File.ReadAllText(file);
            foreach (string pattern in forbidden)
            {
                text.Should().NotContain(pattern, $"{Path.GetRelativePath(root, file)} must take item and recipe definitions from items.json/recipes.json");
            }
        }
    }

    [Fact]
    public void Apply_ServerChangedEnhancementChances_DesktopShowsTheSyncedTable()
    {
        SyncedItemCatalog synced = new();
        string enhancement = System.Text.Encoding.UTF8.GetString(s_enhancementJson)
            .Replace("\"success\": 1000,", "\"success\": 990,", StringComparison.Ordinal)
            .Replace("\"noChange\": 0,", "\"noChange\": 10,", StringComparison.Ordinal);

        synced.Apply(Config(3, s_itemsJson, enhancement: System.Text.Encoding.UTF8.GetBytes(enhancement))).Should().BeTrue();

        synced.Rules.Enhancement.Steps[0].SuccessPermille.Should().Be(990, "forge chances come from the synced enhancement.json (ADR-024), never from the client build");
        synced.Apply(new ConfigCatalog(4, [
            new ConfigCatalogEntry("items", 0, 1, s_itemsJson),
            new ConfigCatalogEntry("recipes", 0, 1, s_recipesJson),
        ])).Should().BeFalse("enhancement.json must sync with items.json");
    }

    [Fact]
    public void Apply_ServerChangedRepairMaterials_DesktopQuotesTheSyncedTable()
    {
        SyncedItemCatalog synced = new();
        string repair = System.Text.Encoding.UTF8.GetString(s_repairJson)
            .Replace("\"item\": \"iron_ingot\", \"qty\": 2 } ],\n      \"items\": [ \"iron_sword\"", "\"item\": \"iron_ingot\", \"qty\": 6 } ],\n      \"items\": [ \"iron_sword\"", StringComparison.Ordinal);

        synced.Apply(Config(6, s_itemsJson, repair: System.Text.Encoding.UTF8.GetBytes(repair))).Should().BeTrue();

        uint sword = synced.Rules.ItemCatalog.ResolveId("iron_sword");
        synced.Rules.Repair.TryQuoteFull(new ItemSlot(sword, 1, Durability: 1f), RepairVenue.Station, out RepairQuote quote).Should().Be(RepairError.None);
        quote.GetMaterial(0).Quantity.Should().Be(9, "6 ingots at tier 2 (150 %) come from the synced repair.json (ADR-028), never from the client build");
        synced.Apply(new ConfigCatalog(7, [
            new ConfigCatalogEntry("items", 0, 1, s_itemsJson),
            new ConfigCatalogEntry("recipes", 0, 1, s_recipesJson),
            new ConfigCatalogEntry("enhancement", 0, 1, s_enhancementJson),
        ])).Should().BeFalse("repair.json must sync with items.json");
    }

    private static ConfigCatalog Config(uint version, byte[] items, byte[]? recipes = null, byte[]? enhancement = null, byte[]? repair = null)
    {
        byte[] recipeBytes = recipes ?? s_recipesJson;
        byte[] enhancementBytes = enhancement ?? s_enhancementJson;
        byte[] repairBytes = repair ?? s_repairJson;
        return new ConfigCatalog(
            version,
            [
                new ConfigCatalogEntry("items", ConfigContentHash.Compute(items), version, items),
                new ConfigCatalogEntry("recipes", ConfigContentHash.Compute(recipeBytes), version, recipeBytes),
                new ConfigCatalogEntry("enhancement", ConfigContentHash.Compute(enhancementBytes), version, enhancementBytes),
                new ConfigCatalogEntry("repair", ConfigContentHash.Compute(repairBytes), version, repairBytes),
            ]);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Carbon.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Carbon.slnx not found above the test output directory.");
    }
}
