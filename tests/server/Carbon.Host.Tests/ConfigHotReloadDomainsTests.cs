// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Carbon.Abstractions.World;
using Carbon.Application.World;
using Carbon.Contracts.Config.S2C;
using Carbon.Economy;
using Carbon.Infrastructure.Persistence.Sqlite;
using Carbon.Items.Definitions;
using Carbon.Server.Configuration;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using NSubstitute;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>
/// ADR-014 hot reload: one reload swaps the manifest served to clients and the tables the server runs, or neither.
/// </summary>
public sealed partial class ConfigHotReloadDomainsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"carbon-hot-reload-{Guid.NewGuid():N}");
    private readonly string _configDir;
    private readonly SqliteConnectionFactory _db;
    private readonly ConfigRepository _config;
    private readonly AuthoritativeWorld _world;

    public ConfigHotReloadDomainsTests()
    {
        _configDir = Path.Combine(_root, "config");
        _ = Directory.CreateDirectory(_configDir);
        string source = GameEconomy.FindConfigDirectory(AppContext.BaseDirectory)
            ?? throw new DirectoryNotFoundException("config folder not found next to the test assembly");
        foreach (string file in Directory.GetFiles(source, "*.json"))
        {
            File.Copy(file, Path.Combine(_configDir, Path.GetFileName(file)));
        }

        _db = new SqliteConnectionFactory($"Data Source={Path.Combine(_root, "world.db")}");
        _config = ConfigRepository.Load(_configDir);
        ServerGameData data = ServerGameData.Load(_config.Catalog);
        _world = new AuthoritativeWorld(
            1337UL,
            32f,
            0.0008f,
            30,
            new SqliteChunkDeltaRepository(_db),
            new SqliteCharacterStore(_db),
            Substitute.For<IWorldOutboundSink>(),
            time: null,
            data.Needs,
            data.Food,
            data.Economy,
            ServerGameData.LoadStructures(_config.Catalog),
            ServerGameData.LoadCreatures(_config.Catalog));
        ServerGameData.BindHotReload(_config, _world);
    }

    [Fact]
    public void Reload_ChangedItemsAndNeeds_SwapsManifestAndRunningTablesTogether()
    {
        GameEconomy economyBefore = _world.Economy!;
        ulong itemsHashBefore = this.HashOf("items");
        uint manifestBefore = _config.ManifestVersion;
        ItemCatalogEntry first = economyBefore.Items.All[0];
        float foodMaxBefore = _world.NeedsConfig.FoodMax;
        int raisedFor = 0;
        _config.ManifestChanged += _ =>
        {
            // When clients are told, the server is already running the new tables.
            _world.Economy!.Items.TryGet(first.Id, out ItemCatalogEntry? live).Should().BeTrue();
            live!.Name.Should().Be("Hot Reloaded Name");
            raisedFor++;
        };

        this.Edit("items.json", json => json.Replace($"\"name\": \"{first.Name}\"", "\"name\": \"Hot Reloaded Name\"", StringComparison.Ordinal));
        this.Edit("needs.json", json => FoodMaxPattern().Replace(json, $"\"foodMax\": {foodMaxBefore + 50f}"));
        ConfigReloadResult result = _config.Reload();

        result.Changed.Should().BeEquivalentTo("items", "needs");
        raisedFor.Should().Be(1);
        _config.ManifestVersion.Should().Be(manifestBefore + 1);
        this.HashOf("items").Should().NotBe(itemsHashBefore);
        using ConfigManifest manifest = _config.CreateManifest();
        manifest.ManifestVersion.Should().Be(_config.ManifestVersion);
        manifest.ContentHashes[Array.IndexOf(manifest.Names, "items")].Should().Be(this.HashOf("items"));

        _world.Economy.Should().NotBeSameAs(economyBefore);
        _world.Economy!.Items.TryGet(first.Id, out ItemCatalogEntry? swapped).Should().BeTrue();
        swapped!.Name.Should().Be("Hot Reloaded Name");
        _world.ItemRules.Items.GetRequired(first.Id).DisplayName.Should().Be("Hot Reloaded Name", "item stats and recipes swap with the economy");
        _world.NeedsConfig.FoodMax.Should().Be(foodMaxBefore + 50f);
        _world.FoodCatalog.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Reload_SemanticallyInvalidDocument_RejectsWholeReloadAndKeepsManifestAndTables()
    {
        GameEconomy economyBefore = _world.Economy!;
        uint manifestBefore = _config.ManifestVersion;
        ulong itemsHashBefore = this.HashOf("items");
        bool raised = false;
        _config.ManifestChanged += _ => raised = true;

        // Valid JSON (passes canonicalization), but two items share an id: the economy loader rejects it.
        ItemCatalogEntry first = economyBefore.Items.All[0];
        ItemCatalogEntry second = economyBefore.Items.All[1];
        this.Edit("items.json", json => json.Replace($"\"id\": {second.Id},", $"\"id\": {first.Id},", StringComparison.Ordinal));
        this.Edit("needs.json", json => json + " ");

        Action reload = () => _config.Reload();

        reload.Should().Throw<InvalidDataException>();
        raised.Should().BeFalse();
        _config.ManifestVersion.Should().Be(manifestBefore);
        this.HashOf("items").Should().Be(itemsHashBefore);
        _world.Economy.Should().BeSameAs(economyBefore);
    }

    [Theory]
    [InlineData("structures")]
    [InlineData("monsters")]
    [InlineData("spawning")]
    public void Reload_RestartRequiredDocumentChanged_IsRejectedAndSwapsNothing(string document)
    {
        uint manifestBefore = _config.ManifestVersion;
        GameEconomy economyBefore = _world.Economy!;
        this.Edit($"{document}.json", json => json.Insert(json.IndexOf('{', StringComparison.Ordinal) + 1, "\"_note\": \"edited\","));
        this.Edit("items.json", json => json.Replace("\"name\": \"", "\"name\": \"X", StringComparison.Ordinal));

        Action reload = () => _config.Reload();

        reload.Should().Throw<InvalidDataException>().WithMessage($"*{document}*restart*");
        _config.ManifestVersion.Should().Be(manifestBefore);
        _world.Economy.Should().BeSameAs(economyBefore, "a rejected reload swaps no domain, even ones that were valid");
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }

    [GeneratedRegex("\"foodMax\"\\s*:\\s*[0-9.]+")]
    private static partial Regex FoodMaxPattern();

    private ulong HashOf(string name) => _config.Entries.Single(e => e.Name == name).ContentHash;

    private void Edit(string fileName, Func<string, string> change)
    {
        string path = Path.Combine(_configDir, fileName);
        string before = File.ReadAllText(path, Encoding.UTF8);
        string after = change(before);
        after.Should().NotBe(before, $"the test edit must change {fileName}");
        File.WriteAllText(path, after, new UTF8Encoding(false));
    }
}
