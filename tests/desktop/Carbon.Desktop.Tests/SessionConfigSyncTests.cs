// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Configuration.Catalog;
using Carbon.Contracts.Config.S2C;
using Carbon.Desktop.Networking.Session;
using FluentAssertions;
using Nalix.SDK.Transport;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>
/// Characterization tests for the ADR-014 config sync that moved out of <c>MultiplayerSession</c> (audit round 2, SRP-1).
/// An empty manifest is always "cache current", so <c>ConfigSyncClient</c> raises Ready synchronously with no download;
/// a manifest naming a document the empty cache lacks needs a download, which exercises the transport path.
/// </summary>
public sealed class SessionConfigSyncTests : IDisposable
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(5);

    private readonly string _cacheDirectory = Path.Combine(Path.GetTempPath(), $"carbon-config-sync-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_cacheDirectory))
        {
            Directory.Delete(_cacheDirectory, recursive: true);
        }
    }

    [Fact]
    public void WaitForGameConfigAsync_BeforeAnyManifest_StaysPendingAndGameConfigIsNull()
    {
        SessionConfigSync sync = this.Create();

        Task<ConfigCatalog> ready = sync.WaitForGameConfigAsync(default);

        ready.IsCompleted.Should().BeFalse();
        sync.GameConfig.Should().BeNull();
    }

    [Fact]
    public async Task OnConfigManifest_CacheCurrent_AppliesCatalogBeforePublishingIt()
    {
        List<string> order = [];
        SessionConfigSync sync = this.Create(apply: catalog => order.Add(catalog.ManifestVersion == 3u ? "apply(v3)" : "apply(?)"));
        sync.Changed += catalog => order.Add(sync.GameConfig == catalog ? "changed(published)" : "changed(unpublished)");

        using ConfigManifest manifest = EmptyManifest(3);
        sync.OnConfigManifest(manifest);
        ConfigCatalog catalog = await sync.WaitForGameConfigAsync(default).WaitAsync(s_timeout);

        catalog.ManifestVersion.Should().Be(3u);
        sync.GameConfig.Should().BeSameAs(catalog);
        order.Should().Equal("apply(v3)", "changed(published)");
    }

    [Fact]
    public async Task OnConfigManifest_HotReloadWithoutReset_RepublishesAndRearmsTheReadyTask()
    {
        List<uint> applied = [];
        List<uint> changed = [];
        SessionConfigSync sync = this.Create(apply: catalog => applied.Add(catalog.ManifestVersion));
        sync.Changed += catalog => changed.Add(catalog.ManifestVersion);

        using ConfigManifest first = EmptyManifest(1);
        sync.OnConfigManifest(first);
        using ConfigManifest second = EmptyManifest(2);
        sync.OnConfigManifest(second);

        // The first ready task already completed with v1; a hot reload must hand later waiters the new catalog.
        (await sync.WaitForGameConfigAsync(default).WaitAsync(s_timeout)).ManifestVersion.Should().Be(2u);
        sync.GameConfig!.ManifestVersion.Should().Be(2u);
        applied.Should().Equal(1u, 2u);
        changed.Should().Equal(1u, 2u);
    }

    [Fact]
    public async Task ResetForNewConnection_AfterSync_WaitsForTheNextManifest()
    {
        SessionConfigSync sync = this.Create();
        using ConfigManifest first = EmptyManifest(1);
        sync.OnConfigManifest(first);
        _ = await sync.WaitForGameConfigAsync(default).WaitAsync(s_timeout);

        sync.ResetForNewConnection();
        Task<ConfigCatalog> next = sync.WaitForGameConfigAsync(default);

        next.IsCompleted.Should().BeFalse();
        using ConfigManifest second = EmptyManifest(2);
        sync.OnConfigManifest(second);
        (await next.WaitAsync(s_timeout)).ManifestVersion.Should().Be(2u);
    }

    [Fact]
    public async Task ResetForNewConnection_BeforeFirstSync_KeepsTheSamePendingTask()
    {
        SessionConfigSync sync = this.Create();
        Task<ConfigCatalog> before = sync.WaitForGameConfigAsync(default);

        sync.ResetForNewConnection();

        // Only a completed latch is replaced; waiters from before the reconnect still get the next catalog.
        sync.WaitForGameConfigAsync(default).IsCompleted.Should().BeFalse();
        using ConfigManifest manifest = EmptyManifest(5);
        sync.OnConfigManifest(manifest);
        before.IsCompletedSuccessfully.Should().BeTrue();
        (await before.WaitAsync(s_timeout)).ManifestVersion.Should().Be(5u);
    }

    [Fact]
    public async Task OnConfigManifest_DownloadNeededWithoutTransport_StopsQuietlyWithoutPublishing()
    {
        int transportLookups = 0;
        bool applied = false;
        SessionConfigSync sync = this.Create(
            apply: _ => applied = true,
            currentTcp: () =>
            {
                _ = Interlocked.Increment(ref transportLookups);
                return null;
            });

        using ConfigManifest manifest = ManifestNeedingDownload(7);
        sync.OnConfigManifest(manifest);
        Task<ConfigCatalog> ready = sync.WaitForGameConfigAsync(default);
        await Task.Delay(50);

        // No transport means "try again on the next connection": no fault, no publish, the latch stays pending.
        transportLookups.Should().Be(1);
        ready.IsCompleted.Should().BeFalse();
        applied.Should().BeFalse();
        sync.GameConfig.Should().BeNull();
    }

    [Fact]
    public async Task WaitForGameConfigAsync_Cancelled_ThrowsWithoutCompletingTheLatch()
    {
        SessionConfigSync sync = this.Create();
        using CancellationTokenSource cts = new();

        Task<ConfigCatalog> cancelled = sync.WaitForGameConfigAsync(cts.Token);
        await cts.CancelAsync();

        Func<Task> act = () => cancelled;
        _ = await act.Should().ThrowAsync<OperationCanceledException>();
        using ConfigManifest manifest = EmptyManifest(1);
        sync.OnConfigManifest(manifest);
        (await sync.WaitForGameConfigAsync(default).WaitAsync(s_timeout)).ManifestVersion.Should().Be(1u);
    }

    private static ConfigManifest EmptyManifest(uint version) => new()
    {
        ManifestVersion = version,
        Names = [],
        ContentHashes = [],
        ByteLengths = [],
        Versions = [],
    };

    private static ConfigManifest ManifestNeedingDownload(uint version) => new()
    {
        ManifestVersion = version,
        Names = ["items"],
        ContentHashes = [0x1234_5678_9ABC_DEF0UL],
        ByteLengths = [16],
        Versions = [1],
    };

    private SessionConfigSync Create(Action<ConfigCatalog>? apply = null, Func<TcpSession?>? currentTcp = null) =>
        new(_cacheDirectory, currentTcp ?? (() => null), apply ?? (_ => { }));
}
