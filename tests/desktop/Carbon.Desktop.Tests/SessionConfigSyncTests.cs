// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Carbon.Configuration.Catalog;
using Carbon.Contracts.Config.S2C;
using Carbon.Desktop.Networking.Session;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

public sealed class SessionConfigSyncTests : IDisposable
{
    private readonly string _cacheDirectory = Path.Combine(Path.GetTempPath(), $"carbon-config-sync-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_cacheDirectory))
        {
            Directory.Delete(_cacheDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task OnConfigManifest_CacheCurrent_AppliesCatalogBeforePublishingIt()
    {
        List<string> order = [];
        SessionConfigSync sync = new(_cacheDirectory, () => null, _ => order.Add("apply"));
        sync.Changed += catalog => order.Add(sync.GameConfig == catalog ? "changed(published)" : "changed(unpublished)");

        using ConfigManifest first = EmptyManifest(3);
        sync.OnConfigManifest(first);
        ConfigCatalog catalog = await sync.WaitForGameConfigAsync(default).WaitAsync(TimeSpan.FromSeconds(5));

        catalog.ManifestVersion.Should().Be(3u);
        sync.GameConfig.Should().BeSameAs(catalog);
        order.Should().Equal("apply", "changed(published)");
    }

    [Fact]
    public async Task ResetForNewConnection_AfterSync_WaitsForTheNextManifest()
    {
        SessionConfigSync sync = new(_cacheDirectory, () => null, _ => { });
        using ConfigManifest first = EmptyManifest(1);
        sync.OnConfigManifest(first);
        _ = await sync.WaitForGameConfigAsync(default).WaitAsync(TimeSpan.FromSeconds(5));

        sync.ResetForNewConnection();
        Task<ConfigCatalog> next = sync.WaitForGameConfigAsync(default);

        next.IsCompleted.Should().BeFalse();
        using ConfigManifest second = EmptyManifest(2);
        sync.OnConfigManifest(second);
        (await next.WaitAsync(TimeSpan.FromSeconds(5))).ManifestVersion.Should().Be(2u);
    }

    private static ConfigManifest EmptyManifest(uint version) => new()
    {
        ManifestVersion = version,
        Names = [],
        ContentHashes = [],
        ByteLengths = [],
        Versions = [],
    };
}
