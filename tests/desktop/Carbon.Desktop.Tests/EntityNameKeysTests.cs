// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using Carbon.Configuration;
using Carbon.Configuration.Catalog;
using Carbon.Contracts.Entities;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.Localization;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>Client-side name keys for replicated monsters, NPCs and loot (ADR-023 D4).</summary>
public sealed class EntityNameKeysTests
{
    private static readonly string s_configDirectory = Path.Combine(FindRepositoryRoot(), "resources", "config");

    [Fact]
    public void Resolve_Monster_MapsArchetypeToMonsterIdKey()
    {
        EntityNameKeys names = new(new SyncedItemCatalog());
        names.Apply(Config(1, "monsters")).Should().BeTrue();

        names.Resolve(EntityKind.Monster, 101, LootCurrency.None, null).Should().Be("monster.wild_slime.name");
        names.Resolve(EntityKind.Monster, 9999, LootCurrency.None, null).Should().BeNull("an unknown archetype keeps the server name");
    }

    [Fact]
    public void Resolve_Npc_DerivesKeyFromCatalogNameAndCachesIt()
    {
        EntityNameKeys names = new(new SyncedItemCatalog());

        string? first = names.Resolve(EntityKind.Npc, 204, LootCurrency.None, "Master Blacksmith");

        first.Should().Be("npc.master_blacksmith.name");
        names.Resolve(EntityKind.Npc, 204, LootCurrency.None, "Master Blacksmith").Should().BeSameAs(first, "keys are built once per archetype");
    }

    [Fact]
    public void Resolve_Loot_UsesItemCodeOrCurrencyKey()
    {
        SyncedItemCatalog items = new();
        items.Apply(Config(1, "items", "recipes", "enhancement", "repair")).Should().BeTrue();
        EntityNameKeys names = new(items);

        names.Resolve(EntityKind.LootDrop, 3, LootCurrency.None, null).Should().Be("item.iron_sword.name");
        names.Resolve(EntityKind.LootDrop, 0, LootCurrency.Gold, null).Should().Be(EntityNameKeys.GoldKey);
        names.Resolve(EntityKind.LootDrop, 0, LootCurrency.Diamond, null).Should().Be(EntityNameKeys.DiamondKey);
    }

    [Fact]
    public void Resolve_Player_KeepsTheSentName() =>
        new EntityNameKeys(new SyncedItemCatalog()).Resolve(EntityKind.Player, 1, LootCurrency.None, null).Should().BeNull();

    [Fact]
    public void Apply_WithMalformedMonsters_KeepsThePreviousMap()
    {
        EntityNameKeys names = new(new SyncedItemCatalog());
        names.Apply(Config(1, "monsters")).Should().BeTrue();

        byte[] broken = "{\"monsters\":[{\"id\":\"x\"}]}"u8.ToArray();
        names.Apply(new ConfigCatalog(2, [new ConfigCatalogEntry("monsters", ConfigContentHash.Compute(broken), 2, broken)])).Should().BeFalse();

        names.Resolve(EntityKind.Monster, 101, LootCurrency.None, null).Should().Be("monster.wild_slime.name");
    }

    [Theory]
    [InlineData("Village Guard", "npc.village_guard.name")]
    [InlineData("Harbour Master", "npc.harbour_master.name")]
    public void NpcKey_SnakeCasesTheCatalogName(string specName, string expected) =>
        EntityNameKeys.NpcKey(specName).Should().Be(expected);

    private static ConfigCatalog Config(uint version, params string[] documents)
    {
        ConfigCatalogEntry[] entries = new ConfigCatalogEntry[documents.Length];
        for (int i = 0; i < documents.Length; i++)
        {
            byte[] utf8 = File.ReadAllBytes(Path.Combine(s_configDirectory, documents[i] + ".json"));
            entries[i] = new ConfigCatalogEntry(documents[i], ConfigContentHash.Compute(utf8), version, utf8);
        }

        return new ConfigCatalog(version, entries);
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
