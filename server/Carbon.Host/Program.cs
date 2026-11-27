// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.Configuration;
using Carbon.Abstractions.Persistence;
using Carbon.Application.Identity;
using Carbon.Application.World;
using Carbon.Economy;
using Carbon.Infrastructure.Configuration;
using Carbon.Infrastructure.Persistence.Postgres;
using Carbon.Infrastructure.Persistence.Sqlite;
using Carbon.Infrastructure.Security;
using Carbon.Server.Configuration;
using Microsoft.Extensions.Logging;
using Nalix.Hosting;
using Npgsql;

namespace Carbon.Host;

/// <summary>
/// Entry point for the Native-AOT Carbon dedicated game server host.
/// </summary>
public static class Program
{
    /// <summary>
    /// Config documents the server uses but never sends to clients: drop rates, price tunables and spawn rules are
    /// server decisions a client has no reason to see.
    /// </summary>
    private static readonly string[] s_serverOnlyDocuments = ["economy", "loot", "spawning"];

    /// <summary>Boots migrations, initializes the authoritative world, and runs listeners until shutdown.</summary>
    public static async Task<int> Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        HostRuntimeOptions options = HostRuntimeOptions.FromEnvironment();

        using ILoggerFactory loggerFactory = LoggerFactory.Create(b =>
            b.SetMinimumLevel(LogLevel.Information).AddConsole());

        ILogger logger = loggerFactory.CreateLogger("Carbon.Host");

        using NpgsqlDataSource? pgSource = options.Provider == DatabaseProvider.Postgres
            ? PgDataSourceFactory.Create(options.ConnectionString)
            : null;
        using SqliteConnectionFactory? sqliteFactory = options.Provider == DatabaseProvider.Sqlite
            ? new SqliteConnectionFactory(options.ConnectionString)
            : null;

        return await RunWithDatabaseAsync(args, options, loggerFactory, logger, pgSource, sqliteFactory)
            .ConfigureAwait(false);
    }

    private static async Task<int> RunWithDatabaseAsync(
        string[] args,
        HostRuntimeOptions options,
        ILoggerFactory loggerFactory,
        ILogger logger,
        NpgsqlDataSource? pgSource,
        SqliteConnectionFactory? sqliteFactory)
    {

        IDatabaseMigrator migrator;
        IAccountStore accountStore;
        ICharacterStore characterStore;
        IChunkDeltaRepository chunkDeltaRepo;

        if (pgSource is not null)
        {
            migrator = new PgSchemaMigrator(pgSource);
            accountStore = new PgAccountStore(pgSource);
            characterStore = new PgCharacterStore(pgSource);
            chunkDeltaRepo = new PgChunkDeltaRepository(pgSource);
        }
        else
        {
            migrator = new SqliteSchemaMigrator(sqliteFactory!);
            accountStore = new SqliteAccountStore(sqliteFactory!);
            characterStore = new SqliteCharacterStore(sqliteFactory!);
            chunkDeltaRepo = new SqliteChunkDeltaRepository(sqliteFactory!);
        }

        await migrator.MigrateAsync().ConfigureAwait(false);

        Pbkdf2PasswordHasher hasher = new();
        SessionTransportRegistry transportRegistry = new();
        WorldOutboundSink outboundSink = new(transportRegistry, loggerFactory.CreateLogger<WorldOutboundSink>());
        // Gameplay config is server-authoritative (ADR-014): the server builds its domains from the same snapshot it
        // serves to clients, so both sides always run on identical bytes.
        ConfigRepository config = ConfigRepository.Load(ResolveConfigDirectory(options.ConfigDirectory), s_serverOnlyDocuments);
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Loaded {Count} config documents from {Directory} (manifest v{Version}).",
                config.Entries.Count,
                config.Directory,
                config.ManifestVersion);
        }

        ServerGameData gameData = ServerGameData.Load(config.Catalog);

        AuthoritativeWorld world = new(
            options.WorldSeed,
            options.TileSize,
            options.Frequency,
            options.TickRate,
            chunkDeltaRepo,
            characterStore,
            outboundSink,
            time: null,
            gameData.Needs,
            gameData.Food,
            gameData.Economy,
            ServerGameData.LoadStructures(config.Catalog),
            ServerGameData.LoadCreatures(config.Catalog),
            gameData.Progression);
        ServerGameData.BindHotReload(config, world);
        using AuthService authService = new(accountStore, characterStore, hasher, time: null, () => world.ItemRules, () => world.Progression);

        if (options.DebugOverrides.IsActive)
        {
            string applied = world.ApplyDebugOverrides(options.DebugOverrides);
            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning("DEBUG world overrides active ({Overrides}); never use this host build in production.", applied);
            }
        }

        await world.InitializeFromStoreAsync().ConfigureAwait(false);

        if (args.Length > 0 && string.Equals(args[0], "--verify-boot", StringComparison.Ordinal))
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "Boot verification succeeded (Provider={Provider}, Seed={Seed}, GeneratorVersion={Version}).",
                    options.Provider,
                    world.WorldSeed,
                    world.GeneratorVersion);
            }

            return 0;
        }

        // Hot reload swaps the served manifest and the running domains together (ServerGameData.BindHotReload).
        using ConfigHotReloadWatcher? configWatcher = options.ConfigHotReload ? new(config, logger) : null;
        NetworkApplication app = Startup.Build(options, authService, world, outboundSink, transportRegistry, logger, config);
        await using ConfiguredAsyncDisposable appScope = app.ConfigureAwait(false);

        using CancellationTokenSource cts = new();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        WorldTickHostedRunner tickRunner = new(world, loggerFactory.CreateLogger<WorldTickHostedRunner>());
        Task tickTask = tickRunner.RunAsync(cts.Token);

        if (logger.IsEnabled(LogLevel.Information))
        {
            string pubKeyHex = Convert.ToHexString(Nalix.Runtime.Handlers.HandshakeHandlers.ServerPublicKey.AsSpan());
            logger.LogInformation(
                "Carbon.Host ready (Provider={Provider}, Seed={Seed}, TickRate={TickRate}Hz, TCP={TcpPort}, UDP={UdpPort}, WS={WsPort}{WsPath}, PublicKey={PublicKey}).",
                options.Provider,
                options.WorldSeed,
                options.TickRate,
                options.TcpPort,
                options.UdpPort,
                options.WebSocketPort,
                options.WebSocketPath,
                pubKeyHex);
        }

        await app.RunAsync(cts.Token).ConfigureAwait(false);
        await cts.CancelAsync().ConfigureAwait(false);
        await tickTask.ConfigureAwait(false);
        return 0;
    }

    /// <summary>
    /// Returns <paramref name="configured"/> when set; otherwise the <c>resources/config</c> (or a <c>config</c> folder copied
    /// next to the binaries) found from the executable directory, then from the working directory.
    /// </summary>
    private static string ResolveConfigDirectory(string configured) =>
        configured.Length > 0
            ? configured
            : GameEconomy.FindConfigDirectory(AppContext.BaseDirectory)
                ?? GameEconomy.FindConfigDirectory(Environment.CurrentDirectory)
                ?? throw new InvalidOperationException("resources/config was not found; set CARBON_CONFIG_DIR.");
}
