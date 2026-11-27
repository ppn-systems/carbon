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

/// <summary>
/// Pure <c>Npgsql</c> account store with zero reflection and Native AOT compatibility.
/// </summary>
public sealed class PostgresAccountStore : IAccountStore
{
    private const string FindSql = """
        SELECT id, username, normalized_username, password_hash, password_salt, created_at_utc, is_banned
        FROM accounts
        WHERE normalized_username = @norm
        LIMIT 1;
        """;

    private const string FindByIdSql = """
        SELECT id, username, normalized_username, password_hash, password_salt, created_at_utc, is_banned
        FROM accounts
        WHERE id = @id
        LIMIT 1;
        """;

    private const string InsertSql = """
        INSERT INTO accounts (id, username, normalized_username, password_hash, password_salt, created_at_utc, is_banned)
        VALUES (@id, @name, @norm, @hash, @salt, @created, @banned)
        ON CONFLICT (normalized_username) DO NOTHING;
        """;

    private readonly NpgsqlDataSource _dataSource;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresAccountStore"/> class.
    /// </summary>
    /// <param name="dataSource">PostgreSQL data source.</param>
    public PostgresAccountStore(NpgsqlDataSource dataSource) =>
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));

    /// <inheritdoc/>
    public async Task<AccountRecord?> FindByUsernameAsync(string normalizedUsername, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedUsername);

        NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable connScope = conn.ConfigureAwait(false);

        NpgsqlCommand cmd = new(FindSql, conn);
        await using ConfiguredAsyncDisposable cmdScope = cmd.ConfigureAwait(false);
        _ = cmd.Parameters.Add(new NpgsqlParameter("norm", NpgsqlDbType.Varchar) { Value = normalizedUsername });
        return await ReadAccountAsync(cmd, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<AccountRecord?> FindByIdAsync(Guid accountId, CancellationToken ct = default)
    {
        NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable connScope = conn.ConfigureAwait(false);

        NpgsqlCommand cmd = new(FindByIdSql, conn);
        await using ConfiguredAsyncDisposable cmdScope = cmd.ConfigureAwait(false);
        _ = cmd.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = accountId });
        return await ReadAccountAsync(cmd, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<bool> TryCreateAsync(AccountRecord account, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable connScope = conn.ConfigureAwait(false);

        NpgsqlCommand cmd = new(InsertSql, conn);
        await using ConfiguredAsyncDisposable cmdScope = cmd.ConfigureAwait(false);
        _ = cmd.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = account.Id });
        _ = cmd.Parameters.Add(new NpgsqlParameter("name", NpgsqlDbType.Varchar) { Value = account.Username });
        _ = cmd.Parameters.Add(new NpgsqlParameter("norm", NpgsqlDbType.Varchar) { Value = account.NormalizedUsername });
        _ = cmd.Parameters.Add(new NpgsqlParameter("hash", NpgsqlDbType.Bytea) { Value = PasswordHashColumn.Write(account.PasswordHash) });
        _ = cmd.Parameters.Add(new NpgsqlParameter("salt", NpgsqlDbType.Bytea) { Value = account.PasswordSalt });
        _ = cmd.Parameters.Add(new NpgsqlParameter("created", NpgsqlDbType.TimestampTz) { Value = account.CreatedAtUtc });
        _ = cmd.Parameters.Add(new NpgsqlParameter("banned", NpgsqlDbType.Boolean) { Value = account.IsBanned });

        int affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return affected == 1;
    }

    private static async Task<AccountRecord?> ReadAccountAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        DbDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable readerScope = reader.ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        Bytes32 hash = PasswordHashColumn.Read(await reader.GetFieldValueAsync<byte[]>(3, ct).ConfigureAwait(false));
        byte[] salt = await reader.GetFieldValueAsync<byte[]>(4, ct).ConfigureAwait(false);
        DateTimeOffset created = await reader.GetFieldValueAsync<DateTimeOffset>(5, ct).ConfigureAwait(false);

        return new AccountRecord(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            hash,
            salt,
            created,
            reader.GetBoolean(6));
    }
}
