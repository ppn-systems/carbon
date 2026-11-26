// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Abstractions.Configuration;

/// <summary>
/// Selects the Native AOT persistence provider used by the server host.
/// </summary>
public enum DatabaseProvider : byte
{
    /// <summary>Embedded SQLite via <c>Microsoft.Data.Sqlite</c> (default for local dev/tests).</summary>
    Sqlite = 0,

    /// <summary>Production PostgreSQL via pure <c>Npgsql</c> (<c>NpgsqlSlimDataSourceBuilder</c>).</summary>
    Postgres = 1,
}
