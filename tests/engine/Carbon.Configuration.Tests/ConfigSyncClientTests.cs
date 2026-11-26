// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Carbon.Configuration.Caching;
using Carbon.Configuration.Catalog;
using Carbon.Configuration.Sync;
using Carbon.Contracts.Config.S2C;
using FluentAssertions;
using Xunit;
using static Carbon.Configuration.Tests.ConfigTestServer;

namespace Carbon.Configuration.Tests;

public sealed class ConfigSyncClientTests
{
    private static readonly byte[] s_items = Utf8("[{\"id\":1,\"name\":\"stick\"}]");
    private static readonly byte[] s_monsters = Utf8("[{\"id\":7,\"hp\":30}]");

    private readonly InMemoryConfigCacheStorage _storage = new();

    [Fact]
    public void HandleManifest_CacheHit_SendsNoRequestAndRaisesReady()
    {
        _storage.Write("items", s_items);
        _storage.Write("monsters", s_monsters);
        ConfigSyncClient client = this.CreateClient();
        ConfigCatalog? ready = null;
        client.Ready += catalog => ready = catalog;

        string[] request = client.HandleManifest(Manifest(4, ("items", s_items, 1), ("monsters", s_monsters, 2)));

        request.Should().BeEmpty();
        client.State.Should().Be(ConfigSyncState.Ready);
        ready.Should().NotBeNull();
        ready!.ManifestVersion.Should().Be(4u);
        ready.GetUtf8("items").ToArray().Should().Equal(s_items);
        ready.GetRequired("monsters").Version.Should().Be(2u);
    }

    [Fact]
    public void HandleManifest_ChangedHash_RequestsOnlyChangedDocumentAndPersistsIt()
    {
        _storage.Write("items", Utf8("[{\"id\":1,\"name\":\"old\"}]"));
        _storage.Write("monsters", s_monsters);
        ConfigSyncClient client = this.CreateClient();

        string[] request = client.HandleManifest(Manifest(2, ("items", s_items, 2), ("monsters", s_monsters, 1)));

        request.Should().Equal("items");
        client.State.Should().Be(ConfigSyncState.Downloading);

        Feed(client, Chunks("items", s_items, 2, 1024)).Should().BeEmpty();

        client.State.Should().Be(ConfigSyncState.Ready);
        client.Catalog!.GetUtf8("items").ToArray().Should().Equal(s_items);
        _storage.TryRead("items", out byte[]? cached).Should().BeTrue();
        cached.Should().Equal(s_items);
    }

    [Fact]
    public void HandleManifest_MissingDocument_IsRequested()
    {
        ConfigSyncClient client = this.CreateClient();

        client.HandleManifest(Manifest(1, ("items", s_items, 1))).Should().Equal("items");
    }

    [Fact]
    public void HandleManifest_CorruptedCacheEntry_IsEvictedAndRedownloaded()
    {
        byte[] corrupted = (byte[])s_items.Clone();
        corrupted[3] ^= 0x20;
        _storage.Write("items", corrupted);
        ConfigSyncClient client = this.CreateClient();

        client.HandleManifest(Manifest(1, ("items", s_items, 1))).Should().Equal("items");
        _storage.TryRead("items", out _).Should().BeFalse();
    }

    [Fact]
    public void HandleDocument_CorruptedPayload_IsRejectedNotPersistedAndReRequested()
    {
        ConfigSyncClient client = this.CreateClient();
        client.HandleManifest(Manifest(1, ("items", s_items, 1)));
        byte[] tampered = (byte[])s_items.Clone();
        tampered[5] = (byte)'9';

        List<ConfigDocument> stream = Chunks("items", tampered, 1, 8, advertised: s_items);
        stream.Add(Terminator());
        string[] retry = Feed(client, stream);

        retry.Should().Equal("items");
        client.State.Should().Be(ConfigSyncState.Downloading);
        client.Catalog.Should().BeNull();
        _storage.Count.Should().Be(0);

        List<ConfigDocument> good = Chunks("items", s_items, 1, 8);
        good.Add(Terminator());
        Feed(client, good).Should().BeEmpty();
        client.State.Should().Be(ConfigSyncState.Ready);
    }

    [Fact]
    public void HandleDocument_ChunkMetadataDisagreesWithManifest_IsRejected()
    {
        ConfigSyncClient client = this.CreateClient();
        client.HandleManifest(Manifest(1, ("items", s_items, 1)));
        byte[] other = Utf8("[{\"id\":2,\"name\":\"rock\"}]");

        // Self-consistent bytes and hash, but not the hash the manifest announced.
        List<ConfigDocument> stream = Chunks("items", other, 1, 1024);
        stream.Add(Terminator());

        Feed(client, stream).Should().Equal("items");
        client.State.Should().Be(ConfigSyncState.Downloading);
        _storage.Count.Should().Be(0);
    }

    [Fact]
    public void HandleDocument_OutOfOrderChunks_AreRejected()
    {
        ConfigSyncClient client = this.CreateClient();
        client.HandleManifest(Manifest(1, ("items", s_items, 1)));
        List<ConfigDocument> stream = Chunks("items", s_items, 1, 4);
        stream.Reverse();
        stream.Add(Terminator());

        Feed(client, stream).Should().Equal("items");
    }

    [Fact]
    public void HandleDocument_MultiChunkDocuments_ReassembleToReady()
    {
        ConfigSyncClient client = this.CreateClient();
        client.HandleManifest(Manifest(1, ("items", s_items, 1), ("monsters", s_monsters, 1))).Should().HaveCount(2);

        List<ConfigDocument> stream = Chunks("items", s_items, 1, 5);
        stream.AddRange(Chunks("monsters", s_monsters, 1, 3));
        stream.Add(Terminator());

        Feed(client, stream).Should().BeEmpty();
        client.State.Should().Be(ConfigSyncState.Ready);
        client.Catalog!.GetUtf8("monsters").ToArray().Should().Equal(s_monsters);
    }

    [Fact]
    public void HandleDocument_UnrequestedDocument_IsIgnored()
    {
        _storage.Write("monsters", s_monsters);
        ConfigSyncClient client = this.CreateClient();
        client.HandleManifest(Manifest(1, ("items", s_items, 1), ("monsters", s_monsters, 1)));

        Feed(client, Chunks("loot", Utf8("[]"), 1, 1024));

        client.State.Should().Be(ConfigSyncState.Downloading);
        _storage.TryRead("loot", out _).Should().BeFalse();
    }

    [Fact]
    public void HandleDocument_RepeatedCorruption_FaultsAfterMaxAttempts()
    {
        ConfigSyncClient client = new(new ConfigDocumentStore(_storage), maxAttemptsPerDocument: 2);
        string? fault = null;
        client.Faulted += reason => fault = reason;
        client.HandleManifest(Manifest(1, ("items", s_items, 1)));
        byte[] tampered = (byte[])s_items.Clone();
        tampered[^2] = (byte)' ';

        for (int attempt = 0; attempt < 2; attempt++)
        {
            List<ConfigDocument> stream = Chunks("items", tampered, 1, 1024, advertised: s_items);
            stream.Add(Terminator());
            _ = Feed(client, stream);
        }

        client.State.Should().Be(ConfigSyncState.Faulted);
        fault.Should().Contain("items");
    }

    [Fact]
    public void HandleManifest_MalformedManifest_Faults()
    {
        ConfigSyncClient client = this.CreateClient();
        ConfigManifest manifest = Manifest(1, ("items", s_items, 1));
        manifest.Versions = [];

        client.HandleManifest(manifest).Should().BeEmpty();
        client.State.Should().Be(ConfigSyncState.Faulted);

        ConfigManifest badName = Manifest(1, ("../items", s_items, 1));
        client.HandleManifest(badName).Should().BeEmpty();
        client.State.Should().Be(ConfigSyncState.Faulted);
    }

    [Fact]
    public void HandleManifest_NewManifestAfterReady_DownloadsOnlyChangedDocument()
    {
        ConfigSyncClient client = this.CreateClient();
        client.HandleManifest(Manifest(1, ("items", s_items, 1), ("monsters", s_monsters, 1)));
        List<ConfigDocument> stream = Chunks("items", s_items, 1, 1024);
        stream.AddRange(Chunks("monsters", s_monsters, 1, 1024));
        Feed(client, stream);
        client.State.Should().Be(ConfigSyncState.Ready);

        byte[] newMonsters = Utf8("[{\"id\":7,\"hp\":45}]");
        client.HandleManifest(Manifest(2, ("items", s_items, 1), ("monsters", newMonsters, 2))).Should().Equal("monsters");

        Feed(client, Chunks("monsters", newMonsters, 2, 1024));
        client.Catalog!.ManifestVersion.Should().Be(2u);
        client.Catalog.GetUtf8("monsters").ToArray().Should().Equal(newMonsters);
    }

    private static string[] Feed(ConfigSyncClient client, IEnumerable<ConfigDocument> packets)
    {
        string[] last = [];
        foreach (ConfigDocument packet in packets)
        {
            string[] retry = client.HandleDocument(packet);
            if (retry.Length > 0)
            {
                last = retry;
            }
        }

        return last;
    }

    private ConfigSyncClient CreateClient() => new(new ConfigDocumentStore(_storage));
}
