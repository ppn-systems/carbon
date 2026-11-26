// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Carbon.Configuration;
using Carbon.Configuration.Catalog;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Items.Catalog;
using Carbon.Items.Definitions;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>The desktop reads item names, stats and recipes from the server's synced <c>items.json</c> and <c>recipes.json</c>.</summary>
[Collection(LiveHostGroup.Name)]
public sealed class SyncedItemCatalogTests
{
    private static readonly byte[] s_itemsJson = File.ReadAllBytes(Path.Combine(FindRepositoryRoot(), "resources", "config", "items.json"));
    private static readonly byte[] s_recipesJson = File.ReadAllBytes(Path.Combine(FindRepositoryRoot(), "resources", "config", "recipes.json"));

    [Fact]
    public void Apply_SyncedItemsDocument_ServesNamesAndStacksForItemsTheClientNeverCompiled()
    {
        try
        {
            SyncedItemCatalog.Apply(Config(1, s_itemsJson)).Should().BeTrue();

            ItemCatalog catalog = SyncedItemCatalog.Current!;
            ItemCatalogEntry beyondStarter = catalog.All.ToArray().First(static i => i.Id >= 100);
            SyncedItemCatalog.GetDisplayName(beyondStarter.Id).Should().Be(beyondStarter.Name);
            SyncedItemCatalog.GetMaxStackSize(beyondStarter.Id).Should().Be(beyondStarter.MaxStack);
            SyncedItemCatalog.GetDisplayName(0U).Should().Be("Empty");
            SyncedItemCatalog.GetDisplayName(uint.MaxValue).Should().Be($"Item #{uint.MaxValue}");
            StarterGameCatalog.Items.Contains(beyondStarter.Id).Should().BeTrue("the synced catalog is installed as the active item table");
            StarterGameCatalog.Recipes.Count.Should().Be(StarterGameCatalog.RecipeCatalog.Count);
        }
        finally
        {
            RestoreShippedConfig();
        }
    }

    [Fact]
    public void Apply_MissingOrInvalidItemsDocument_KeepsPreviousCatalog()
    {
        try
        {
            SyncedItemCatalog.Apply(Config(1, s_itemsJson)).Should().BeTrue();
            ItemCatalog? before = SyncedItemCatalog.Current;
            ItemArchetype axeBefore = StarterGameCatalog.Items.GetRequired(StarterGameCatalog.WoodAxe);

            SyncedItemCatalog.Apply(new ConfigCatalog(2, [])).Should().BeFalse();
            SyncedItemCatalog.Apply(new ConfigCatalog(3, [new ConfigCatalogEntry("items", 0, 1, "{\"items\":[{\"id\":0}]}"u8.ToArray())])).Should().BeFalse();
            SyncedItemCatalog.Apply(new ConfigCatalog(4, [new ConfigCatalogEntry("items", 0, 1, s_itemsJson)])).Should().BeFalse("recipes.json must sync with items.json so recipe ids match the server");

            SyncedItemCatalog.Current.Should().BeSameAs(before);
            StarterGameCatalog.Items.GetRequired(StarterGameCatalog.WoodAxe).Should().BeSameAs(axeBefore);
        }
        finally
        {
            RestoreShippedConfig();
        }
    }

    [Fact]
    public void Apply_ServerChangedCombatStatsAndRecipes_DesktopUsesSyncedValuesNotCompiledOnes()
    {
        // The server edits items.json/recipes.json; the client must show and predict with the synced values.
        string items = System.Text.Encoding.UTF8.GetString(s_itemsJson)
            .Replace("\"baseDamage\": 34.0", "\"baseDamage\": 999.0", StringComparison.Ordinal);
        string recipes = System.Text.Encoding.UTF8.GetString(s_recipesJson)
            .Replace("\"code\": \"wood_plank\"", "\"code\": \"wood_plank_synced\"", StringComparison.Ordinal);
        try
        {
            SyncedItemCatalog.Apply(Config(5, System.Text.Encoding.UTF8.GetBytes(items), System.Text.Encoding.UTF8.GetBytes(recipes))).Should().BeTrue();

            StarterGameCatalog.Items.GetRequired(StarterGameCatalog.IronSword).BaseDamage.Should().Be(999f, "iron sword damage comes from the synced items.json");
            StarterGameCatalog.GetWeaponBaseDamage(StarterGameCatalog.IronSword).Should().Be(999f);
            StarterGameCatalog.RecipeCatalog.TryGetByCode("wood_plank_synced", out _).Should().BeTrue("recipes come from the synced recipes.json");
        }
        finally
        {
            RestoreShippedConfig();
        }

        StarterGameCatalog.Items.GetRequired(StarterGameCatalog.IronSword).BaseDamage.Should().Be(34f);
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

    private static ConfigCatalog Config(uint version, byte[] items, byte[]? recipes = null)
    {
        byte[] recipeBytes = recipes ?? s_recipesJson;
        return new ConfigCatalog(
            version,
            [
                new ConfigCatalogEntry("items", ConfigContentHash.Compute(items), version, items),
                new ConfigCatalogEntry("recipes", ConfigContentHash.Compute(recipeBytes), version, recipeBytes),
            ]);
    }

    private static void RestoreShippedConfig() => SyncedItemCatalog.Apply(Config(100, s_itemsJson)).Should().BeTrue();

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
