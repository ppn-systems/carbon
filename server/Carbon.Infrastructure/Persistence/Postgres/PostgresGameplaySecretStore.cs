// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.Persistence;
using Npgsql;
using NpgsqlTypes;

namespace Carbon.Infrastructure.Persistence.Postgres;

/// <summary>PostgreSQL store of the world's gameplay secret (<c>gameplay_secret</c>, ADR-026).</summary>
public sealed class PostgresGameplaySecretStore : IGameplaySecretStore
{
    private const string InsertIfAbsentSql = """
        INSERT INTO gameplay_secret (id, secret, created_at_utc)
        VALUES (1, @secret, @created)
        ON CONFLICT (id) DO NOTHING;
        """;

    private const string SelectSql = "SELECT secret FROM gameplay_secret WHERE id = 1;";

    private readonly NpgsqlDataSource _dataSource;

    /// <summary>Initializes a new instance of the <see cref="PostgresGameplaySecretStore"/> class.</summary>
    /// <param name="dataSource">Npgsql data source.</param>
    public PostgresGameplaySecretStore(NpgsqlDataSource dataSource) =>
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));

    /// <inheritdoc/>
    public async Task<byte[]> GetOrCreateAsync(ReadOnlyMemory<byte> candidate, CancellationToken ct = default)
    {
        if (candidate.IsEmpty)
        {
            throw new ArgumentException("The candidate secret must not be empty.", nameof(candidate));
        }

        NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable connScope = conn.ConfigureAwait(false);
        NpgsqlTransaction tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable txScope = tx.ConfigureAwait(false);

        NpgsqlCommand insert = new(InsertIfAbsentSql, conn, tx);
        await using (ConfiguredAsyncDisposable insertScope = insert.ConfigureAwait(false))
        {
            _ = insert.Parameters.Add(new NpgsqlParameter("secret", NpgsqlDbType.Bytea) { Value = candidate.ToArray() });
            _ = insert.Parameters.Add(new NpgsqlParameter("created", NpgsqlDbType.TimestampTz) { Value = DateTimeOffset.UtcNow });
            _ = await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        byte[] secret;
        NpgsqlCommand select = new(SelectSql, conn, tx);
        await using (ConfiguredAsyncDisposable selectScope = select.ConfigureAwait(false))
        {
            secret = await select.ExecuteScalarAsync(ct).ConfigureAwait(false) as byte[]
                ?? throw new InvalidOperationException("gameplay_secret has no row after insert.");
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
        return secret;
    }
}
