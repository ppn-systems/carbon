// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.Persistence;
using Nalix.Abstractions.Primitives;
using Npgsql;
using NpgsqlTypes;

namespace Carbon.Infrastructure.Persistence.Postgres;

/// <summary>PostgreSQL store of remember-me token hashes (<c>remember_tokens</c>, ADR-037).</summary>
public sealed class PgRememberTokenStore : IRememberTokenStore
{
    private const string InsertSql = """
        INSERT INTO remember_tokens (token_hash, account_id, created_at_utc, expires_at_utc)
        VALUES (@hash, @account, @created, @expires);
        DELETE FROM remember_tokens
        WHERE account_id = @account
          AND token_hash NOT IN (
              SELECT token_hash FROM remember_tokens
              WHERE account_id = @account
              ORDER BY created_at_utc DESC
              LIMIT @keep);
        """;

    private const string FindSql = """
        SELECT account_id, expires_at_utc FROM remember_tokens WHERE token_hash = @hash LIMIT 1;
        """;

    private const string DeleteSql = "DELETE FROM remember_tokens WHERE token_hash = @hash;";

    private readonly NpgsqlDataSource _dataSource;

    /// <summary>Initializes a new instance of the <see cref="PgRememberTokenStore"/> class.</summary>
    /// <param name="dataSource">PostgreSQL data source.</param>
    public PgRememberTokenStore(NpgsqlDataSource dataSource) =>
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));

    /// <inheritdoc/>
    public async Task InsertAsync(RememberTokenRecord record, int keepPerAccount, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentOutOfRangeException.ThrowIfLessThan(keepPerAccount, 1);

        NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable connScope = conn.ConfigureAwait(false);

        NpgsqlCommand cmd = new(InsertSql, conn);
        await using ConfiguredAsyncDisposable cmdScope = cmd.ConfigureAwait(false);
        _ = cmd.Parameters.Add(new NpgsqlParameter("hash", NpgsqlDbType.Bytea) { Value = record.TokenHash.ToByteArray() });
        _ = cmd.Parameters.Add(new NpgsqlParameter("account", NpgsqlDbType.Uuid) { Value = record.AccountId });
        _ = cmd.Parameters.Add(new NpgsqlParameter("created", NpgsqlDbType.TimestampTz) { Value = DateTimeOffset.UtcNow });
        _ = cmd.Parameters.Add(new NpgsqlParameter("expires", NpgsqlDbType.TimestampTz) { Value = record.ExpiresAtUtc.ToUniversalTime() });
        _ = cmd.Parameters.Add(new NpgsqlParameter("keep", NpgsqlDbType.Integer) { Value = keepPerAccount });
        _ = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<RememberTokenRecord?> FindAsync(Bytes32 tokenHash, CancellationToken ct = default)
    {
        NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable connScope = conn.ConfigureAwait(false);

        NpgsqlCommand cmd = new(FindSql, conn);
        await using ConfiguredAsyncDisposable cmdScope = cmd.ConfigureAwait(false);
        _ = cmd.Parameters.Add(new NpgsqlParameter("hash", NpgsqlDbType.Bytea) { Value = tokenHash.ToByteArray() });

        DbDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable readerScope = reader.ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        DateTimeOffset expires = await reader.GetFieldValueAsync<DateTimeOffset>(1, ct).ConfigureAwait(false);
        return new RememberTokenRecord(tokenHash, reader.GetGuid(0), expires);
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(Bytes32 tokenHash, CancellationToken ct = default)
    {
        NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable connScope = conn.ConfigureAwait(false);

        NpgsqlCommand cmd = new(DeleteSql, conn);
        await using ConfiguredAsyncDisposable cmdScope = cmd.ConfigureAwait(false);
        _ = cmd.Parameters.Add(new NpgsqlParameter("hash", NpgsqlDbType.Bytea) { Value = tokenHash.ToByteArray() });
        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0;
    }
}
