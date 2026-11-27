// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Configuration;
using Carbon.Configuration.Caching;
using Carbon.Configuration.Catalog;
using Carbon.Contracts.Config.S2C;
using Carbon.Desktop.Networking.Session;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>
/// Behaviour tests for the ADR-014 config sync that moved out of <c>MultiplayerSession</c> (audit round 2, SRP-1), driven
/// through a fake server that records every document request. The fake's stream completes synchronously unless told to
/// yield, so a manifest is fully processed by the time <see cref="SessionConfigSync.OnConfigManifest"/> returns.
/// </summary>
public sealed class SessionConfigSyncTests : IDisposable
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(5);
    private static readonly byte[] s_items = Utf8("[{\"id\":1,\"name\":\"stick\"},{\"id\":2,\"name\":\"stone\"}]");
    private static readonly byte[] s_itemsV2 = Utf8("[{\"id\":1,\"name\":\"stick\"},{\"id\":2,\"name\":\"flint\"},{\"id\":3,\"name\":\"rope\"}]");
    private static readonly byte[] s_monsters = Utf8("[{\"id\":7,\"hp\":30}]");
    private static readonly byte[] s_recipes = Utf8("[{\"id\":40,\"out\":3}]");

    private readonly string _cacheDirectory = Path.Combine(Path.GetTempPath(), $"carbon-config-sync-{Guid.NewGuid():N}");
    private readonly FakeConfigServer _server = new();

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
    public async Task OnConfigManifest_AllDocumentsCached_AppliesFromCacheWithoutRequestingAnything()
    {
        this.Cache("items", s_items);
        this.Cache("monsters", s_monsters);
        this.Cache("recipes", s_recipes);
        List<ConfigCatalog> applied = [];
        SessionConfigSync sync = this.Create(apply: applied.Add);

        using ConfigManifest manifest = Manifest(4, ("items", s_items, 1), ("monsters", s_monsters, 2), ("recipes", s_recipes, 1));
        sync.OnConfigManifest(manifest);
        ConfigCatalog catalog = await sync.WaitForGameConfigAsync(default).WaitAsync(s_timeout);

        _server.Requests.Should().BeEmpty();
        applied.Should().ContainSingle().Which.Should().BeSameAs(catalog);
        catalog.ManifestVersion.Should().Be(4u);
        catalog.GetUtf8("items").ToArray().Should().Equal(s_items);
        catalog.GetUtf8("monsters").ToArray().Should().Equal(s_monsters);
        catalog.GetRequired("monsters").Version.Should().Be(2u);
        catalog.GetUtf8("recipes").ToArray().Should().Equal(s_recipes);
    }

    [Fact]
    public async Task OnConfigManifest_OneHashMismatch_RequestsOnlyThatDocumentAndAppliesAfterItsChunksArrive()
    {
        this.Cache("items", s_items);
        this.Cache("monsters", s_monsters);
        _server.Publish("items", s_itemsV2, version: 2);
        List<ConfigCatalog> applied = [];
        SessionConfigSync sync = this.Create(apply: applied.Add);

        using ConfigManifest manifest = Manifest(9, ("items", s_itemsV2, 2), ("monsters", s_monsters, 1));
        sync.OnConfigManifest(manifest);
        ConfigCatalog catalog = await sync.WaitForGameConfigAsync(default).WaitAsync(s_timeout);

        _server.Requests.Should().ContainSingle();
        _server.Requests[0].ManifestVersion.Should().Be(9u);
        _server.Requests[0].Names.Should().Equal("items");
        _server.ChunksServed.Should().BeGreaterThan(1, "the document is split into several chunks");
        applied.Should().ContainSingle();
        catalog.GetUtf8("items").ToArray().Should().Equal(s_itemsV2);
        catalog.GetUtf8("monsters").ToArray().Should().Equal(s_monsters);
        this.CachedBytes("items").Should().Equal(s_itemsV2, "a verified download replaces the stale cache entry");
    }

    [Theory]
    [InlineData("truncated")]
    [InlineData("corrupted")]
    public async Task OnConfigManifest_DamagedCacheFile_IsReRequestedAndNeverApplied(string damage)
    {
        byte[] damaged = damage == "truncated" ? s_items[..^5] : [.. s_items.Select(static (b, i) => i == 3 ? (byte)(b ^ 0x20) : b)];
        this.Cache("items", damaged);
        _server.Publish("items", s_items, version: 1);
        List<ConfigCatalog> applied = [];
        SessionConfigSync sync = this.Create(apply: applied.Add);

        using ConfigManifest manifest = Manifest(1, ("items", s_items, 1));
        sync.OnConfigManifest(manifest);
        ConfigCatalog catalog = await sync.WaitForGameConfigAsync(default).WaitAsync(s_timeout);

        _server.Requests.Select(static r => r.Names).Should().ContainSingle().Which.Should().Equal("items");
        catalog.GetUtf8("items").ToArray().Should().Equal(s_items);
        applied.Should().ContainSingle().Which.GetUtf8("items").ToArray().Should().NotEqual(damaged);
        this.CachedBytes("items").Should().Equal(s_items);
    }

    [Fact]
    public async Task OnConfigManifest_DamagedCacheAndNoTransport_PublishesNothing()
    {
        this.Cache("items", s_items[..^5]);
        _server.Connected = false;
        bool applied = false;
        SessionConfigSync sync = this.Create(apply: _ => applied = true);

        using ConfigManifest manifest = Manifest(1, ("items", s_items, 1));
        sync.OnConfigManifest(manifest);
        Task<ConfigCatalog> ready = sync.WaitForGameConfigAsync(default);
        await Task.Delay(50);

        // No transport means "wait for the next connection": one lookup, no fault, nothing published.
        _server.Requests.Should().ContainSingle().Which.Names.Should().Equal("items");
        ready.IsCompleted.Should().BeFalse();
        applied.Should().BeFalse();
        sync.GameConfig.Should().BeNull();
    }

    [Fact]
    public async Task OnConfigManifest_ServerKeepsSendingBadBytes_RetriesThenFaultsTheReadyTask()
    {
        _server.Publish("items", s_items, version: 1);
        _server.CorruptedRequests = int.MaxValue;
        bool applied = false;
        SessionConfigSync sync = this.Create(apply: _ => applied = true);

        using ConfigManifest manifest = Manifest(1, ("items", s_items, 1));
        sync.OnConfigManifest(manifest);
        Func<Task> wait = () => sync.WaitForGameConfigAsync(default).WaitAsync(s_timeout);

        _ = await wait.Should().ThrowAsync<InvalidOperationException>().WithMessage("*items*");
        _server.Requests.Should().HaveCount(3, "ConfigSyncClient allows three attempts per document by default");
        applied.Should().BeFalse();
        sync.GameConfig.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OnConfigManifest_ServerSendsBadBytesOnce_RetriesTheDocumentAndApplies(bool streamEndsWithoutTerminator)
    {
        // Without a terminator the client must close the stream itself, or the retry is never requested.
        _server.Publish("items", s_items, version: 1);
        _server.CorruptedRequests = 1;
        _server.OmitTerminator = streamEndsWithoutTerminator;
        List<ConfigCatalog> applied = [];
        SessionConfigSync sync = this.Create(apply: applied.Add);

        using ConfigManifest manifest = Manifest(1, ("items", s_items, 1));
        sync.OnConfigManifest(manifest);
        ConfigCatalog catalog = await sync.WaitForGameConfigAsync(default).WaitAsync(s_timeout);

        _server.Requests.Select(static r => r.Names).Should().HaveCount(2).And.AllSatisfy(names => names.Should().Equal("items"));
        applied.Should().ContainSingle();
        catalog.GetUtf8("items").ToArray().Should().Equal(s_items);
    }

    [Fact]
    public async Task OnConfigManifest_OlderManifestVersion_IsStillApplied_CurrentBehaviour()
    {
        // Characterization, not a spec: neither ConfigSyncClient nor this class orders manifests by version; the last
        // manifest pushed wins. A restarted server legitimately starts again at a lower version.
        this.Cache("items", s_items);
        SessionConfigSync sync = this.Create();

        using ConfigManifest newer = Manifest(5, ("items", s_items, 1));
        sync.OnConfigManifest(newer);
        using ConfigManifest older = Manifest(3, ("items", s_items, 1));
        sync.OnConfigManifest(older);

        sync.GameConfig!.ManifestVersion.Should().Be(3u);
        (await sync.WaitForGameConfigAsync(default).WaitAsync(s_timeout)).ManifestVersion.Should().Be(3u);
    }

    [Fact]
    public async Task OnConfigManifest_HotReloadInWorld_SwapsTheWholeCatalogOnceAndRaisesChangedOnce()
    {
        this.Cache("items", s_items);
        this.Cache("monsters", s_monsters);
        _server.Publish("items", s_itemsV2, version: 2);
        _server.YieldBetweenChunks = true;
        List<uint> applied = [];
        List<uint> changed = [];
        List<string> seenWhileApplying = [];
        SessionConfigSync? sync = null;
        sync = this.Create(apply: catalog =>
        {
            applied.Add(catalog.ManifestVersion);

            // A reader during apply still sees the previous catalog, whole: never a mix of old and new documents.
            seenWhileApplying.Add(sync!.GameConfig is { } current ? $"v{current.ManifestVersion}:{Encoding.UTF8.GetString(current.GetUtf8("items"))}" : "none");
        });
        TaskCompletionSource<ConfigCatalog> reloaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
        sync.Changed += catalog =>
        {
            changed.Add(catalog.ManifestVersion);
            if (catalog.ManifestVersion == 2u)
            {
                _ = reloaded.TrySetResult(catalog);
            }
        };

        using ConfigManifest first = Manifest(1, ("items", s_items, 1), ("monsters", s_monsters, 1));
        sync.OnConfigManifest(first);
        ConfigCatalog initial = await sync.WaitForGameConfigAsync(default).WaitAsync(s_timeout);
        using ConfigManifest second = Manifest(2, ("items", s_itemsV2, 2), ("monsters", s_monsters, 1));
        sync.OnConfigManifest(second);
        ConfigCatalog swapped = await reloaded.Task.WaitAsync(s_timeout);

        applied.Should().Equal(1u, 2u);
        changed.Should().Equal(1u, 2u);
        seenWhileApplying.Should().Equal("none", $"v1:{Encoding.UTF8.GetString(s_items)}");
        sync.GameConfig.Should().BeSameAs(swapped).And.NotBeSameAs(initial);
        swapped.GetUtf8("items").ToArray().Should().Equal(s_itemsV2);
        swapped.GetUtf8("monsters").ToArray().Should().Equal(s_monsters);
        initial.GetUtf8("items").ToArray().Should().Equal(s_items, "the previous catalog object is never mutated");
        (await sync.WaitForGameConfigAsync(default).WaitAsync(s_timeout)).Should().BeSameAs(swapped);
    }

    [Fact]
    public async Task OnConfigManifest_ApplyThrowsOnHotReload_KeepsThePreviousCatalogPublished()
    {
        this.Cache("items", s_items);
        this.Cache("monsters", s_monsters);
        List<uint> changed = [];
        SessionConfigSync sync = this.Create(apply: catalog =>
        {
            if (catalog.ManifestVersion == 2u)
            {
                throw new FormatException("bad items table");
            }
        });
        sync.Changed += catalog => changed.Add(catalog.ManifestVersion);

        using ConfigManifest first = Manifest(1, ("items", s_items, 1));
        sync.OnConfigManifest(first);
        ConfigCatalog published = await sync.WaitForGameConfigAsync(default).WaitAsync(s_timeout);
        using ConfigManifest second = Manifest(2, ("items", s_items, 1), ("monsters", s_monsters, 1));
        sync.OnConfigManifest(second);

        sync.GameConfig.Should().BeSameAs(published);
        changed.Should().Equal(1u);
        (await sync.WaitForGameConfigAsync(default).WaitAsync(s_timeout)).Should().BeSameAs(published);
    }

    [Fact]
    public async Task OnConfigManifest_ApplyThrowsOnFirstSync_FaultsTheReadyTaskAndPublishesNothing()
    {
        this.Cache("items", s_items);
        SessionConfigSync sync = this.Create(apply: _ => throw new FormatException("bad items table"));

        using ConfigManifest manifest = Manifest(1, ("items", s_items, 1));
        sync.OnConfigManifest(manifest);
        Func<Task> wait = () => sync.WaitForGameConfigAsync(default).WaitAsync(s_timeout);

        _ = await wait.Should().ThrowAsync<InvalidOperationException>().WithMessage("*bad items table*");
        sync.GameConfig.Should().BeNull();
    }

    [Fact]
    public async Task ResetForNewConnection_AfterSync_WaitsForTheNextManifest()
    {
        SessionConfigSync sync = this.Create();
        using ConfigManifest first = Manifest(1);
        sync.OnConfigManifest(first);
        _ = await sync.WaitForGameConfigAsync(default).WaitAsync(s_timeout);

        sync.ResetForNewConnection();
        Task<ConfigCatalog> next = sync.WaitForGameConfigAsync(default);

        next.IsCompleted.Should().BeFalse();
        using ConfigManifest second = Manifest(2);
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
        using ConfigManifest manifest = Manifest(5);
        sync.OnConfigManifest(manifest);
        (await before.WaitAsync(s_timeout)).ManifestVersion.Should().Be(5u);
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
        using ConfigManifest manifest = Manifest(1);
        sync.OnConfigManifest(manifest);
        (await sync.WaitForGameConfigAsync(default).WaitAsync(s_timeout)).ManifestVersion.Should().Be(1u);
    }

    private static byte[] Utf8(string json) => Encoding.UTF8.GetBytes(json);

    private static ConfigManifest Manifest(uint manifestVersion, params (string Name, byte[] Bytes, uint Version)[] documents) => new()
    {
        ManifestVersion = manifestVersion,
        Names = [.. documents.Select(static d => d.Name)],
        ContentHashes = [.. documents.Select(static d => ConfigContentHash.Compute(d.Bytes))],
        ByteLengths = [.. documents.Select(static d => d.Bytes.Length)],
        Versions = [.. documents.Select(static d => d.Version)],
    };

    private SessionConfigSync Create(Action<ConfigCatalog>? apply = null) =>
        new(_cacheDirectory, _server.Open, apply ?? (_ => { }));

    private void Cache(string name, byte[] bytes) => new FileSystemConfigCacheStorage(_cacheDirectory).Write(name, bytes);

    private byte[] CachedBytes(string name) =>
        new FileSystemConfigCacheStorage(_cacheDirectory).TryRead(name, out byte[]? bytes) ? bytes : [];

    /// <summary>Serves published documents in small chunks and records every request it receives.</summary>
    private sealed class FakeConfigServer
    {
        private const int ChunkBytes = 16;

        private readonly Dictionary<string, (byte[] Bytes, uint Version)> _documents = new(StringComparer.Ordinal);

        public List<(uint ManifestVersion, string[] Names)> Requests { get; } = [];

        public int ChunksServed { get; private set; }

        public bool Connected { get; set; } = true;

        public bool YieldBetweenChunks { get; set; }

        /// <summary>Gets or sets how many requests (from the first) are answered with bytes that fail verification.</summary>
        public int CorruptedRequests { get; set; }

        /// <summary>Gets or sets whether the stream ends without the end-of-stream terminator.</summary>
        public bool OmitTerminator { get; set; }

        public void Publish(string name, byte[] bytes, uint version) => _documents[name] = (bytes, version);

        public IAsyncEnumerable<ConfigDocument>? Open(uint manifestVersion, string[] names)
        {
            this.Requests.Add((manifestVersion, [.. names]));
            return this.Connected ? this.StreamAsync(names, corrupt: this.Requests.Count <= this.CorruptedRequests) : null;
        }

        private async IAsyncEnumerable<ConfigDocument> StreamAsync(string[] names, bool corrupt)
        {
            foreach (string name in names)
            {
                (byte[] bytes, uint version) = _documents[name];
                byte[] sent = corrupt ? [.. bytes.Select(static b => (byte)(b ^ 0x01))] : bytes;
                for (int offset = 0; offset < bytes.Length; offset += ChunkBytes)
                {
                    if (this.YieldBetweenChunks)
                    {
                        await Task.Yield();
                    }

                    using ConfigDocument chunk = new()
                    {
                        Name = name,
                        ContentHash = ConfigContentHash.Compute(bytes),
                        Version = version,
                        TotalLength = bytes.Length,
                        Offset = offset,
                        Payload = sent.AsSpan(offset, Math.Min(ChunkBytes, bytes.Length - offset)).ToArray(),
                    };
                    this.ChunksServed++;
                    yield return chunk;
                }
            }

            if (!this.OmitTerminator)
            {
                using ConfigDocument end = new() { IsEndOfStream = true, IsTerminator = true };
                yield return end;
            }
        }
    }
}
