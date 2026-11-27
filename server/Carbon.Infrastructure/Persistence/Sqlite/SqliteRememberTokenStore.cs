// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.Persistence;
using Microsoft.Data.Sqlite;

namespace Carbon.Infrastructure.Persistence.Sqlite;

/// <summary>SQLite store of remember-me token hashes (<c>remember_tokens</c>, ADR-037).</summary>
public sealed class SqliteRememberTokenStore : IRememberTokenStore
{
    private const string InsertSql = """
        INSERT INTO remember_tokens (token_hash, account_id, created_at_utc, expires_at_utc)
        VALUES (@hash, @account, @created, @expires);
        DELETE FROM remember_tokens
        WHERE account_id = @account
          AND token_hash NOT IN (
              SELECT token_hash FROM remember_tokens
              WHERE account_id = @account
              ORDER BY created_at_utc DESC, rowid DESC
              LIMIT @keep);
        """;

    private const string FindSql = """
        SELECT account_id, expires_at_utc FROM remember_tokens WHERE token_hash = @hash LIMIT 1;
        """;

    private const string DeleteSql = "DELETE FROM remember_tokens WHERE token_hash = @hash;";

    private readonly SqliteConnectionFactory _factory;

    /// <summary>Initializes a new instance of the <see cref="SqliteRememberTokenStore"/> class.</summary>
    /// <param name="factory">SQLite connection factory.</param>
    public SqliteRememberTokenStore(SqliteConnectionFactory factory) =>
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <inheritdoc/>
    public async Task InsertAsync(RememberTokenRecord record, int keepPerAccount, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentOutOfRangeException.ThrowIfLessThan(keepPerAccount, 1);

        SqliteConnection conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable connScope = conn.ConfigureAwait(false);

        SqliteCommand cmd = new(InsertSql, conn);
        await using ConfiguredAsyncDisposable cmdScope = cmd.ConfigureAwait(false);
        cmd.Parameters.Add("@hash", SqliteType.Blob).Value = record.TokenHash;
        _ = cmd.Parameters.AddWithValue("@account", record.AccountId.ToString("D"));
        _ = cmd.Parameters.AddWithValue("@created", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        _ = cmd.Parameters.AddWithValue("@expires", record.ExpiresAtUtc.ToUnixTimeMilliseconds());
        _ = cmd.Parameters.AddWithValue("@keep", (long)keepPerAccount);
        _ = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<RememberTokenRecord?> FindAsync(byte[] tokenHash, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);

        SqliteConnection conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable connScope = conn.ConfigureAwait(false);

        SqliteCommand cmd = new(FindSql, conn);
        await using ConfiguredAsyncDisposable cmdScope = cmd.ConfigureAwait(false);
        cmd.Parameters.Add("@hash", SqliteType.Blob).Value = tokenHash;

        SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable readerScope = reader.ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return new RememberTokenRecord(
            tokenHash,
            Guid.Parse(reader.GetString(0)),
            DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)));
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(byte[] tokenHash, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);

        SqliteConnection conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable connScope = conn.ConfigureAwait(false);

        SqliteCommand cmd = new(DeleteSql, conn);
        await using ConfiguredAsyncDisposable cmdScope = cmd.ConfigureAwait(false);
        cmd.Parameters.Add("@hash", SqliteType.Blob).Value = tokenHash;
        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0;
    }
}
