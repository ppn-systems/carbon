// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using Carbon.Abstractions.Configuration;
using Carbon.Primitives.Random;

namespace Carbon.Infrastructure.Configuration;

/// <summary>
/// Runtime configuration loaded from environment variables for Native AOT hosting with zero reflection binders.
/// </summary>
public sealed class HostRuntimeOptions
{
    /// <summary>Gets or sets the active database provider.</summary>
    public DatabaseProvider Provider { get; init; } = DatabaseProvider.Sqlite;

    /// <summary>Gets or sets the provider connection string.</summary>
    public string ConnectionString { get; init; } = "Data Source=carbon_world.db";

    /// <summary>
    /// Gets or sets the deterministic world generation seed. Public: clients receive it to mirror terrain, so it must
    /// never drive a gameplay roll (those use the gameplay secret, ADR-026).
    /// </summary>
    public ulong WorldSeed { get; init; } = 1337UL;

    /// <summary>Gets or sets the physical tile size in world units.</summary>
    public float TileSize { get; init; } = 32f;

    /// <summary>Gets or sets the macro terrain frequency.</summary>
    public float Frequency { get; init; } = 0.0008f;

    /// <summary>Gets or sets the fixed simulation tick rate in Hertz.</summary>
    public ushort TickRate { get; init; } = 30;

    /// <summary>Gets or sets the WebSocket listen host binding (<c>localhost</c> by default, or <c>+</c>).</summary>
    public string WebSocketHost { get; init; } = "localhost";

    /// <summary>Gets or sets the WebSocket listen port (<c>0</c> disables the WebSocket listener).</summary>
    public ushort WebSocketPort { get; init; } = 57206;

    /// <summary>Gets or sets the WebSocket endpoint path.</summary>
    public string WebSocketPath { get; init; } = "/ws/";

    /// <summary>Gets or sets the TCP listen port.</summary>
    public ushort TcpPort { get; init; } = 57207;

    /// <summary>Gets or sets the authenticated UDP (NDP) listen port.</summary>
    public ushort UdpPort { get; init; } = 57208;

    /// <summary>
    /// Gets or sets the gameplay config directory (<c>resources/config</c>, ADR-014).
    /// Empty means "locate <c>resources/config</c> above the working or base directory".
    /// </summary>
    public string ConfigDirectory { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether config file edits are hot-reloaded: the running server domains and the
    /// manifest pushed to clients switch together. On by default; <c>CARBON_CONFIG_HOT_RELOAD=0</c> turns it off.
    /// </summary>
    public bool ConfigHotReload { get; init; } = true;

    /// <summary>
    /// Gets the injected gameplay secret (<c>CARBON_GAMEPLAY_SECRET</c>, 32 hex characters), for tests and replays.
    /// <see langword="null"/> (the default) means the secret persisted in the database, generated on the first boot.
    /// </summary>
    public GameplayKey? GameplaySecret { get; init; }

    /// <summary>
    /// Gets the injected per-boot salt (<c>CARBON_GAMEPLAY_BOOT_SALT</c>, up to 16 hex characters), for replays.
    /// <see langword="null"/> (the default) means a fresh random salt on every start.
    /// </summary>
    public ulong? GameplayBootSalt { get; init; }

    /// <summary>Gets the gameplay key rotation period in seconds of server time (<c>CARBON_GAMEPLAY_EPOCH_SECONDS</c>).</summary>
    public int GameplayEpochSeconds { get; init; } = DefaultGameplayEpochSeconds;

    /// <summary>
    /// Gets the developer-only world overrides (debug spawn tile or biome, start hour). Always empty in
    /// Release builds of the host: the <c>CARBON_DEBUG_*</c> variables are only read by Debug builds.
    /// </summary>
    public WorldDebugOverrides DebugOverrides { get; init; } = new();

    /// <summary>Default gameplay key rotation period: ten minutes.</summary>
    public const int DefaultGameplayEpochSeconds = 600;

    /// <summary>
    /// Loads options from environment variables with safe local SQLite defaults.
    /// </summary>
    public static HostRuntimeOptions FromEnvironment()
    {
        string rawProvider = Environment.GetEnvironmentVariable("CARBON_DB_PROVIDER") ?? string.Empty;
        DatabaseProvider provider = string.Equals(rawProvider.Trim(), "postgres", StringComparison.OrdinalIgnoreCase)
            ? DatabaseProvider.Postgres
            : DatabaseProvider.Sqlite;

        string defaultConn = provider == DatabaseProvider.Postgres
            ? "Host=localhost;Port=5432;Database=carbon;Username=carbon;Password=carbon"
            : "Data Source=carbon_world.db";

        string connectionString = Environment.GetEnvironmentVariable("CARBON_DB_CONNECTION") is { Length: > 0 } conn
            ? conn
            : defaultConn;

        ulong seed = ulong.TryParse(Environment.GetEnvironmentVariable("CARBON_WORLD_SEED"), NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong s)
            ? s
            : 1337UL;

        ushort tickRate = ushort.TryParse(Environment.GetEnvironmentVariable("CARBON_TICK_RATE"), NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort tr) && tr > 0
            ? tr
            : (ushort)30;

        ushort wsPort = ushort.TryParse(Environment.GetEnvironmentVariable("CARBON_WS_PORT"), NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort wp)
            ? wp
            : (ushort)57206;

        ushort tcpPort = ushort.TryParse(Environment.GetEnvironmentVariable("CARBON_TCP_PORT"), NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort tp) && tp > 0
            ? tp
            : (ushort)57207;

        ushort udpPort = ushort.TryParse(Environment.GetEnvironmentVariable("CARBON_UDP_PORT"), NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort up) && up > 0
            ? up
            : (ushort)57208;

        string wsHost = Environment.GetEnvironmentVariable("CARBON_WS_HOST") is { Length: > 0 } host
            ? host.Trim()
            : "localhost";

        string wsPath = Environment.GetEnvironmentVariable("CARBON_WS_PATH") is { Length: > 0 } path
            ? path
            : "/ws/";

        string configDirectory = Environment.GetEnvironmentVariable("CARBON_CONFIG_DIR") is { Length: > 0 } dir
            ? dir.Trim()
            : string.Empty;

        (GameplayKey? gameplaySecret, ulong? gameplayBootSalt, int gameplayEpochSeconds) = ReadGameplayEntropy(Environment.GetEnvironmentVariable);

        return new HostRuntimeOptions
        {
            Provider = provider,
            ConnectionString = connectionString,
            WorldSeed = seed,
            TickRate = tickRate,
            WebSocketHost = wsHost,
            WebSocketPort = wsPort,
            WebSocketPath = wsPath,
            TcpPort = tcpPort,
            UdpPort = udpPort,
            ConfigDirectory = configDirectory,
            ConfigHotReload = !string.Equals(Environment.GetEnvironmentVariable("CARBON_CONFIG_HOT_RELOAD"), "0", StringComparison.Ordinal),
            DebugOverrides = ReadDebugOverrides(Environment.GetEnvironmentVariable),
            GameplaySecret = gameplaySecret,
            GameplayBootSalt = gameplayBootSalt,
            GameplayEpochSeconds = gameplayEpochSeconds,
        };
    }

    /// <summary>
    /// Parses <c>CARBON_GAMEPLAY_SECRET</c>, <c>CARBON_GAMEPLAY_BOOT_SALT</c> and <c>CARBON_GAMEPLAY_EPOCH_SECONDS</c>
    /// (ADR-026). A malformed secret or salt fails the boot instead of silently falling back; error messages never echo
    /// the value.
    /// </summary>
    /// <param name="readEnv">Environment variable reader.</param>
    /// <returns>The injected secret and salt (or <see langword="null"/>) and the rotation period.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a variable is set but malformed.</exception>
    public static (GameplayKey? Secret, ulong? BootSalt, int EpochSeconds) ReadGameplayEntropy(Func<string, string?> readEnv)
    {
        ArgumentNullException.ThrowIfNull(readEnv);
        GameplayKey? secret = null;
        if (readEnv("CARBON_GAMEPLAY_SECRET") is { Length: > 0 } rawSecret)
        {
            string hex = rawSecret.Trim();
            Span<byte> bytes = stackalloc byte[GameplayKey.SizeInBytes];
            if (hex.Length != GameplayKey.SizeInBytes * 2
                || Convert.FromHexString(hex, bytes, out _, out int written) != System.Buffers.OperationStatus.Done
                || written != GameplayKey.SizeInBytes
                || bytes.IndexOfAnyExcept((byte)0) < 0)
            {
                throw new InvalidOperationException(
                    $"CARBON_GAMEPLAY_SECRET must be {GameplayKey.SizeInBytes * 2} hex characters (128 bits) and not all zero.");
            }

            secret = GameplayKey.FromBytes(bytes);
            bytes.Clear();
        }

        ulong? salt = null;
        if (readEnv("CARBON_GAMEPLAY_BOOT_SALT") is { Length: > 0 } rawSalt)
        {
            salt = ulong.TryParse(rawSalt.Trim(), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong parsed)
                ? parsed
                : throw new InvalidOperationException("CARBON_GAMEPLAY_BOOT_SALT must be up to 16 hex characters.");
        }

        int epochSeconds = int.TryParse(readEnv("CARBON_GAMEPLAY_EPOCH_SECONDS"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int es) && es > 0
            ? es
            : DefaultGameplayEpochSeconds;
        return (secret, salt, epochSeconds);
    }

    /// <summary>
    /// Parses the developer-only <c>CARBON_DEBUG_SPAWN_TILE</c> (<c>x,y</c>), <c>CARBON_DEBUG_SPAWN_BIOME</c>
    /// (biome name), <c>CARBON_DEBUG_SPAWN_POI</c> (point of interest type), <c>CARBON_DEBUG_SPAWN_OFFSET</c> (<c>dx,dy</c> tiles),
    /// <c>CARBON_DEBUG_START_HOUR</c> and <c>CARBON_DEBUG_START_LEVEL</c> variables. Release builds ignore them and always return
    /// no overrides.
    /// </summary>
    /// <param name="readEnv">Environment variable reader.</param>
    /// <returns>The overrides (empty when none are set, and always empty in Release builds).</returns>
    public static WorldDebugOverrides ReadDebugOverrides(Func<string, string?> readEnv)
    {
        ArgumentNullException.ThrowIfNull(readEnv);
#if DEBUG
        float? startHour = float.TryParse(readEnv("CARBON_DEBUG_START_HOUR"), NumberStyles.Float, CultureInfo.InvariantCulture, out float h) &&
            h >= 0f && h < 24f
            ? h
            : null;

        return new WorldDebugOverrides
        {
            SpawnTile = TryParsePair(readEnv("CARBON_DEBUG_SPAWN_TILE")),
            SpawnBiome = readEnv("CARBON_DEBUG_SPAWN_BIOME") is { Length: > 0 } biome ? biome.Trim() : null,
            SpawnPoi = readEnv("CARBON_DEBUG_SPAWN_POI") is { Length: > 0 } poi ? poi.Trim() : null,
            SpawnOffset = TryParsePair(readEnv("CARBON_DEBUG_SPAWN_OFFSET")) ?? default,
            StartHour = startHour,
            StartLevel = ushort.TryParse(readEnv("CARBON_DEBUG_START_LEVEL"), NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort lv) && lv > 1
                ? lv
                : null,
            ForgeKit = string.Equals(readEnv("CARBON_DEBUG_FORGE_KIT"), "1", StringComparison.Ordinal),
            StarterItems = ParseStarterItems(readEnv("CARBON_DEBUG_STARTER_ITEMS")),
            WornGear = string.Equals(readEnv("CARBON_DEBUG_WORN_GEAR"), "1", StringComparison.Ordinal),
            WornGearPercents = ParseWornGearPercents(readEnv("CARBON_DEBUG_WORN_GEAR")),
            StatusEffects = readEnv("CARBON_DEBUG_STATUS_EFFECTS")?.Trim() ?? string.Empty,
            TreeStages = string.Equals(readEnv("CARBON_DEBUG_TREE_STAGES"), "1", StringComparison.Ordinal),
        };
#else
        return new WorldDebugOverrides();
#endif
    }

#if DEBUG
    /// <summary>Parses <c>p1,p2,...</c> durability percents (1..100; ADR-035 screenshots); <c>1</c> alone keeps the 40 % kit wear.</summary>
    private static int[] ParseWornGearPercents(string? raw)
    {
        if (raw is null || string.Equals(raw.Trim(), "1", StringComparison.Ordinal))
        {
            return [];
        }

        System.Collections.Generic.List<int> percents = [];
        foreach (string part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int percent) && percent is >= 1 and <= 100)
            {
                percents.Add(percent);
            }
        }

        return [.. percents];
    }

    /// <summary>Parses <c>id[xQty][+Level]</c> entries separated by commas (e.g. <c>3+10,4x40</c>); bad entries are skipped.</summary>
    private static System.Collections.Generic.List<DebugStarterItem> ParseStarterItems(string? raw)
    {
        System.Collections.Generic.List<DebugStarterItem> items = [];
        if (raw is not { Length: > 0 })
        {
            return items;
        }

        foreach (string part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string rest = part;
            byte level = 0;
            int plus = rest.IndexOf('+', StringComparison.Ordinal);
            if (plus >= 0)
            {
                if (!byte.TryParse(rest.AsSpan(plus + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out level) || level > 10)
                {
                    continue;
                }

                rest = rest[..plus];
            }

            int quantity = 1;
            int x = rest.IndexOf('x', StringComparison.Ordinal);
            if (x >= 0)
            {
                if (!int.TryParse(rest.AsSpan(x + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out quantity) || quantity <= 0)
                {
                    continue;
                }

                rest = rest[..x];
            }

            if (uint.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint id) && id > 0)
            {
                items.Add(new DebugStarterItem(id, quantity, level));
            }
        }

        return items;
    }

    private static (int X, int Y)? TryParsePair(string? raw) =>
        raw is { Length: > 0 } &&
        raw.Split(',') is [string a, string b] &&
        int.TryParse(a, NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) &&
        int.TryParse(b, NumberStyles.Integer, CultureInfo.InvariantCulture, out int y)
            ? (x, y)
            : null;
#endif
}
