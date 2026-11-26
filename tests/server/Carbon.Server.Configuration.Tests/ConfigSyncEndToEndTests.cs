// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using Carbon.Configuration.Caching;
using Carbon.Configuration.Sync;
using Carbon.Contracts.Config.C2S;
using Carbon.Contracts.Config.S2C;
using FluentAssertions;
using Xunit;

namespace Carbon.Server.Configuration.Tests;

public sealed class ConfigSyncEndToEndTests : System.IDisposable
{
    private readonly TempConfigDirectory _dir = new();
    private readonly InMemoryConfigCacheStorage _cache = new();

    [Fact]
    public async Task Sync_FirstConnectDownloadsAllThenCacheHitThenHotReloadDownloadsOnlyChanged()
    {
        _dir.Write("items.json", "[{\"id\":1}]");
        _dir.Write("monsters.json", "[{\"id\":7,\"hp\":30}]");
        ConfigRepository repository = ConfigRepository.Load(_dir.Path);

        // First connect: empty cache, everything downloads.
        ConfigSyncClient first = new(new ConfigDocumentStore(_cache));
        (await this.RunAsync(repository, first)).Should().Equal(["items", "monsters"]);
        first.State.Should().Be(ConfigSyncState.Ready);
        first.Catalog!.GetUtf8("monsters").ToArray().Should().Equal(repository.Catalog.GetUtf8("monsters").ToArray());

        // Reconnect: cache matches the manifest, nothing is requested.
        ConfigSyncClient second = new(new ConfigDocumentStore(_cache));
        (await this.RunAsync(repository, second)).Should().BeEmpty();
        second.State.Should().Be(ConfigSyncState.Ready);

        // Hot reload changes one document: the pushed manifest triggers a download of only that one.
        _dir.Write("monsters.json", "[{\"id\":7,\"hp\":45}]");
        repository.Reload().HasChanges.Should().BeTrue();
        (await this.RunAsync(repository, second)).Should().Equal(["monsters"]);
        second.Catalog!.ManifestVersion.Should().Be(2u);
        second.Catalog.GetRequired("monsters").Version.Should().Be(2u);
    }

    public void Dispose() => _dir.Dispose();

    /// <summary>Runs one manifest/request/stream exchange through the wire format; returns the requested names.</summary>
    private async Task<List<string>> RunAsync(ConfigRepository repository, ConfigSyncClient client)
    {
        using ConfigManifest sentManifest = repository.CreateManifest();
        using ConfigManifest manifest = ConfigManifest.Deserialize(sentManifest.Serialize());
        string[] names = client.HandleManifest(manifest);
        List<string> requested = [.. names];
        while (names.Length > 0)
        {
            using ConfigRequest request = new() { ManifestVersion = client.ManifestVersion, Names = names };
            using ConfigRequest received = ConfigRequest.Deserialize(request.Serialize());
            List<ConfigDocument> stream = [];
            (await repository.HandleRequestAsync(received, ConfigRepositoryTests.Collect(stream)).ConfigureAwait(false)).Should().Be(ConfigRequestStatus.Ok);

            names = [];
            foreach (ConfigDocument chunk in stream)
            {
                string[] retry = client.HandleDocument(chunk);
                if (retry.Length > 0)
                {
                    names = retry;
                }
            }
        }

        return requested;
    }
}
