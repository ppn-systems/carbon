// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using Carbon.Abstractions.Configuration;

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

    /// <summary>Gets or sets the deterministic world generation seed.</summary>
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
    /// Gets or sets the gameplay config directory (<c>resources/config</c>, ADR-011).
    /// Empty means "locate <c>resources/config</c> above the working or base directory".
    /// </summary>
    public string ConfigDirectory { get; init; } = string.Empty;

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
        };
    }
}
