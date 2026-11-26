// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Carbon.Infrastructure.Persistence.Sqlite;

/// <summary>
/// Opens configured <see cref="SqliteConnection"/> instances with WAL durability and busy timeouts,
/// keeping a sentinel handle open for in-memory test databases so shared memory persists across calls.
/// </summary>
public sealed class SqliteConnectionFactory : IDisposable
{
    private readonly string _connectionString;
    private readonly SqliteConnection? _memoryKeepAlive;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteConnectionFactory"/> class.
    /// </summary>
    /// <param name="connectionString">SQLite connection string.</param>
    public SqliteConnectionFactory(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;

        if (connectionString.Contains(":memory:", StringComparison.OrdinalIgnoreCase) ||
            connectionString.Contains("Mode=Memory", StringComparison.OrdinalIgnoreCase))
        {
            _memoryKeepAlive = new SqliteConnection(connectionString);
            _memoryKeepAlive.Open();
        }
    }

    /// <summary>
    /// Opens a configured connection asynchronously with WAL mode and foreign keys enabled.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An open <see cref="SqliteConnection"/>.</returns>
    public async Task<SqliteConnection> OpenAsync(CancellationToken ct = default)
    {
        SqliteConnection conn = new(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        SqliteCommand pragma = conn.CreateCommand();
        await using ConfiguredAsyncDisposable pragmaScope = pragma.ConfigureAwait(false);
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        _ = await pragma.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return conn;
    }

    /// <inheritdoc/>
    public void Dispose() => _memoryKeepAlive?.Dispose();
}
