// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Application.Identity;
using Carbon.Application.World;
using Carbon.Configuration.Catalog;
using Carbon.Contracts.Config;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.Identity;
using Carbon.Desktop.Networking.Session;
using Carbon.Host;
using Carbon.Infrastructure.Configuration;
using Carbon.Infrastructure.Persistence.Sqlite;
using Carbon.Infrastructure.Security;
using Carbon.Server.Configuration;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Nalix.Hosting;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>ADR-014 end to end over a live TCP host: manifest push, disk cache, hash-gated downloads, hot reload.</summary>
[Collection(LiveHostGroup.Name)]
public sealed class ConfigSyncLiveTests
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task ConfigSync_OverLiveTcp_DownloadsCachesSkipsUnchangedAndAppliesHotReload()
    {
        string root = Path.Combine(Path.GetTempPath(), $"carbon-config-e2e-{Guid.NewGuid():N}");
        string configDir = Path.Combine(root, "config");
        string cacheDir = Path.Combine(root, "cache");
        string dbPath = Path.Combine(root, "world.db");
        _ = Directory.CreateDirectory(configDir);

        // items.json spans several 16 KiB chunks; monsters.json is a single chunk.
        string items = "[" + string.Join(',', Enumerable.Range(1, 2500).Select(static i => $"{{\"id\":{i},\"name\":\"item-{i}\"}}")) + "]";
        await File.WriteAllTextAsync(Path.Combine(configDir, "items.json"), items).ConfigureAwait(true);
        await File.WriteAllTextAsync(Path.Combine(configDir, "monsters.json"), "[{\"id\":7,\"hp\":30}]").ConfigureAwait(true);

        ushort tcpPort = AllocateFreeTcpPort();
        ushort udpPort = AllocateFreeUdpPort();
        try
        {
            using SqliteConnectionFactory dbFactory = new($"Data Source={dbPath}");
            await new SqliteSchemaMigrator(dbFactory).MigrateAsync().ConfigureAwait(true);
            SqliteAccountStore accounts = new(dbFactory);
            SqliteCharacterStore characters = new(dbFactory);
            using AuthService authService = new(accounts, characters, new Pbkdf2PasswordHasher());
            SessionTransportRegistry transportRegistry = new();
            WorldOutboundSink outboundSink = new(transportRegistry, NullLogger<WorldOutboundSink>.Instance);
            HostRuntimeOptions hostOptions = new() { WebSocketPort = 0, TcpPort = tcpPort, UdpPort = udpPort };
            AuthoritativeWorld world = new(
                hostOptions.WorldSeed,
                hostOptions.TileSize,
                hostOptions.Frequency,
                hostOptions.TickRate,
                new SqliteChunkDeltaRepository(dbFactory),
                characters,
                outboundSink);
            await world.InitializeFromStoreAsync().ConfigureAwait(true);

            ConfigRepository config = ConfigRepository.Load(configDir);
            NetworkApplication app = Startup.Build(hostOptions, authService, world, outboundSink, transportRegistry, NullLogger.Instance, config);
            await app.ActivateAsync(CancellationToken.None).ConfigureAwait(true);

            DesktopOptions optionsA = new() { ServerHost = "127.0.0.1", ServerTcpPort = tcpPort, ConfigCacheDirectory = cacheDir };
            MultiplayerSession clientA = new(optionsA);
            MultiplayerSession clientB = new(optionsA);
            try
            {
                // 1. First sign-in: empty cache, both documents download, verify and land on disk.
                AuthenticationResult registered = await clientA.RegisterAsync("127.0.0.1", tcpPort, "cfg_player_a", "password123").ConfigureAwait(true);
                registered.Succeeded.Should().BeTrue(registered.Message);
                ConfigCatalog first = await clientA.WaitForGameConfigAsync().WaitAsync(s_timeout).ConfigureAwait(true);

                first.ManifestVersion.Should().Be(1u);
                first.GetUtf8("items").ToArray().Should().Equal(config.Catalog.GetUtf8("items").ToArray());
                first.GetUtf8("monsters").ToArray().Should().Equal(config.Catalog.GetUtf8("monsters").ToArray());
                config.Entries.Single(static e => e.Name == "items").ByteLength.Should().BeGreaterThan(ConfigProtocolLimits.ChunkBytes * 2);
                File.Exists(Path.Combine(cacheDir, "items.json")).Should().BeTrue();
                File.Exists(Path.Combine(cacheDir, "monsters.json")).Should().BeTrue();

                // 2. Second client sharing the cache: hashes match, nothing is downloaded or rewritten.
                Dictionary<string, DateTime> stamps = Directory.GetFiles(cacheDir).ToDictionary(static f => f, File.GetLastWriteTimeUtc);
                AuthenticationResult registeredB = await clientB.RegisterAsync("127.0.0.1", tcpPort, "cfg_player_b", "password123").ConfigureAwait(true);
                registeredB.Succeeded.Should().BeTrue(registeredB.Message);
                ConfigCatalog cached = await clientB.WaitForGameConfigAsync().WaitAsync(s_timeout).ConfigureAwait(true);
                cached.GetUtf8("items").ToArray().Should().Equal(first.GetUtf8("items").ToArray());
                Directory.GetFiles(cacheDir).ToDictionary(static f => f, File.GetLastWriteTimeUtc).Should().Equal(stamps);

                // 3. Hot reload: the server pushes the new manifest and the client re-downloads only monsters.
                TaskCompletionSource<ConfigCatalog> reloaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
                clientA.GameConfigChanged += catalog =>
                {
                    if (catalog.ManifestVersion == 2u)
                    {
                        _ = reloaded.TrySetResult(catalog);
                    }
                };
                DateTime itemsStamp = File.GetLastWriteTimeUtc(Path.Combine(cacheDir, "items.json"));
                await File.WriteAllTextAsync(Path.Combine(configDir, "monsters.json"), "[{\"id\":7,\"hp\":45}]").ConfigureAwait(true);
                config.Reload().Changed.Should().Equal("monsters");

                ConfigCatalog updated = await reloaded.Task.WaitAsync(s_timeout).ConfigureAwait(true);
                System.Text.Encoding.UTF8.GetString(updated.GetUtf8("monsters")).Should().Be("[{\"id\":7,\"hp\":45}]");
                updated.GetRequired("monsters").Version.Should().Be(2u);
                clientA.GameConfig.Should().BeSameAs(updated);
                File.GetLastWriteTimeUtc(Path.Combine(cacheDir, "items.json")).Should().Be(itemsStamp);
            }
            finally
            {
                await clientA.DisposeAsync().ConfigureAwait(true);
                await clientB.DisposeAsync().ConfigureAwait(true);
                await app.DeactivateAsync(CancellationToken.None).ConfigureAwait(true);
                await app.DisposeAsync().ConfigureAwait(true);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static ushort AllocateFreeTcpPort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        return (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static ushort AllocateFreeUdpPort()
    {
        using UdpClient udp = new(new IPEndPoint(IPAddress.Loopback, 0));
        return (ushort)((IPEndPoint)udp.Client.LocalEndPoint!).Port;
    }
}
