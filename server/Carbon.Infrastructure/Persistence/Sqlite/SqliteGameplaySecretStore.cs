// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.Persistence;
using Microsoft.Data.Sqlite;

namespace Carbon.Infrastructure.Persistence.Sqlite;

/// <summary>SQLite store of the world's gameplay secret (<c>gameplay_secret</c>, ADR-026).</summary>
public sealed class SqliteGameplaySecretStore : IGameplaySecretStore
{
    private const string InsertIfAbsentSql = """
        INSERT INTO gameplay_secret (id, secret, created_at_utc)
        VALUES (1, @secret, @created)
        ON CONFLICT (id) DO NOTHING;
        """;

    private const string SelectSql = "SELECT secret FROM gameplay_secret WHERE id = 1;";

    private readonly SqliteConnectionFactory _factory;

    /// <summary>Initializes a new instance of the <see cref="SqliteGameplaySecretStore"/> class.</summary>
    /// <param name="factory">SQLite connection factory.</param>
    public SqliteGameplaySecretStore(SqliteConnectionFactory factory) =>
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <inheritdoc/>
    public async Task<byte[]> GetOrCreateAsync(ReadOnlyMemory<byte> candidate, CancellationToken ct = default)
    {
        if (candidate.IsEmpty)
        {
            throw new ArgumentException("The candidate secret must not be empty.", nameof(candidate));
        }

        SqliteConnection conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable connScope = conn.ConfigureAwait(false);
        SqliteTransaction tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable txScope = tx.ConfigureAwait(false);

        SqliteCommand insert = new(InsertIfAbsentSql, conn, tx);
        await using (ConfiguredAsyncDisposable insertScope = insert.ConfigureAwait(false))
        {
            insert.Parameters.Add("@secret", SqliteType.Blob).Value = candidate.ToArray();
            insert.Parameters.Add("@created", SqliteType.Integer).Value = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _ = await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        byte[] secret;
        SqliteCommand select = new(SelectSql, conn, tx);
        await using (ConfiguredAsyncDisposable selectScope = select.ConfigureAwait(false))
        {
            secret = await select.ExecuteScalarAsync(ct).ConfigureAwait(false) as byte[]
                ?? throw new InvalidOperationException("gameplay_secret has no row after insert.");
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
        return secret;
    }
}
