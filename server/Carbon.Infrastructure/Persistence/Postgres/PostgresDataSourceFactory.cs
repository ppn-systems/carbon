// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Npgsql;

namespace Carbon.Infrastructure.Persistence.Postgres;

/// <summary>
/// Builds slim, Native-AOT-friendly PostgreSQL connection pools via <see cref="NpgsqlSlimDataSourceBuilder"/>
/// with explicit minimal codecs and zero reflection ORM overhead.
/// </summary>
public static class PostgresDataSourceFactory
{
    /// <summary>
    /// Creates a configured <see cref="NpgsqlDataSource"/> from a PostgreSQL connection string.
    /// </summary>
    /// <param name="connectionString">Npgsql connection string.</param>
    /// <param name="maxPoolSize">Maximum pooled connections.</param>
    /// <param name="applicationName">Client application identifier logged on the DB server.</param>
    /// <returns>Built <see cref="NpgsqlDataSource"/> ready for injection.</returns>
    public static NpgsqlDataSource Create(
        string connectionString,
        int maxPoolSize = 16,
        string applicationName = "carbon-host")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        NpgsqlConnectionStringBuilder csb = new(connectionString)
        {
            MaxPoolSize = maxPoolSize,
            MinPoolSize = 0,
            ApplicationName = applicationName,
            MaxAutoPrepare = 16,
            AutoPrepareMinUsages = 2,
            KeepAlive = 30,
        };

        NpgsqlSlimDataSourceBuilder builder = new(csb.ConnectionString);
        _ = builder.EnableArrays();
        _ = builder.EnableTransportSecurity();
        return builder.Build();
    }
}
