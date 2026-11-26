// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
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

/// <summary>The desktop reads item names and stack sizes from the server's synced <c>items.json</c>.</summary>
[Collection(LiveHostGroup.Name)]
public sealed class SyncedItemCatalogTests
{
    private static readonly byte[] s_itemsJson = File.ReadAllBytes(Path.Combine(FindRepositoryRoot(), "resources", "config", "items.json"));

    [Fact]
    public void Apply_SyncedItemsDocument_ServesNamesAndStacksForItemsTheClientNeverCompiled()
    {
        ConfigCatalog config = new(1, [new ConfigCatalogEntry("items", ConfigContentHash.Compute(s_itemsJson), 1, s_itemsJson)]);

        SyncedItemCatalog.Apply(config).Should().BeTrue();

        ItemCatalog catalog = SyncedItemCatalog.Current!;
        ItemCatalogEntry beyondStarter = catalog.All.ToArray().First(static i => !StarterGameCatalog.Items.Contains(i.Id));
        SyncedItemCatalog.GetDisplayName(beyondStarter.Id).Should().Be(beyondStarter.Name);
        SyncedItemCatalog.GetMaxStackSize(beyondStarter.Id).Should().Be(beyondStarter.MaxStack);
        SyncedItemCatalog.GetDisplayName(0U).Should().Be("Empty");
        SyncedItemCatalog.GetDisplayName(uint.MaxValue).Should().Be($"Item #{uint.MaxValue}");
    }

    [Fact]
    public void Apply_MissingOrInvalidItemsDocument_KeepsPreviousCatalog()
    {
        ConfigCatalog good = new(1, [new ConfigCatalogEntry("items", 0, 1, s_itemsJson)]);
        SyncedItemCatalog.Apply(good).Should().BeTrue();
        ItemCatalog? before = SyncedItemCatalog.Current;

        SyncedItemCatalog.Apply(new ConfigCatalog(2, [])).Should().BeFalse();
        SyncedItemCatalog.Apply(new ConfigCatalog(3, [new ConfigCatalogEntry("items", 0, 1, "{\"items\":[{\"id\":0}]}"u8.ToArray())])).Should().BeFalse();

        SyncedItemCatalog.Current.Should().BeSameAs(before);
    }

    [Fact]
    public void ItemsDocument_AgreesWithCompiledStarterItemsTheServerStillSimulates()
    {
        // Server crafting/inventory still sizes stacks from StarterGameCatalog while the client predicts from items.json;
        // both must agree for every id the server can hand out.
        ItemCatalog catalog = ItemCatalog.LoadFromJson(System.Text.Encoding.UTF8.GetString(s_itemsJson));
        foreach (ItemArchetype starter in StarterGameCatalog.Items.All)
        {
            catalog.TryGet(starter.Id, out ItemCatalogEntry? entry).Should().BeTrue($"item {starter.Id} ('{starter.Code}') must be in items.json");
            entry!.Code.Should().Be(starter.Code);
            entry.MaxStack.Should().Be(Math.Max(1, starter.MaxStackSize), $"stack size of '{starter.Code}'");
        }
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
