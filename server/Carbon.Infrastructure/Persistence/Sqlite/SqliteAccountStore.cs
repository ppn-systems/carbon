// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.Persistence;
using Microsoft.Data.Sqlite;
using Nalix.Abstractions.Primitives;

namespace Carbon.Infrastructure.Persistence.Sqlite;

/// <summary>
/// Pure SQLite account store using explicit parameterized commands and ordinal <see cref="SqliteDataReader"/> reads.
/// </summary>
public sealed class SqliteAccountStore : IAccountStore
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

    private readonly SqliteConnectionFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteAccountStore"/> class.
    /// </summary>
    /// <param name="factory">SQLite connection factory.</param>
    public SqliteAccountStore(SqliteConnectionFactory factory) =>
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <inheritdoc/>
    public async Task<AccountRecord?> FindByUsernameAsync(string normalizedUsername, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedUsername);

        SqliteConnection conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable connScope = conn.ConfigureAwait(false);

        SqliteCommand cmd = new(FindSql, conn);
        await using ConfiguredAsyncDisposable cmdScope = cmd.ConfigureAwait(false);
        _ = cmd.Parameters.AddWithValue("@norm", normalizedUsername);
        return await ReadAccountAsync(cmd, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<AccountRecord?> FindByIdAsync(Guid accountId, CancellationToken ct = default)
    {
        SqliteConnection conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable connScope = conn.ConfigureAwait(false);

        SqliteCommand cmd = new(FindByIdSql, conn);
        await using ConfiguredAsyncDisposable cmdScope = cmd.ConfigureAwait(false);
        _ = cmd.Parameters.AddWithValue("@id", accountId.ToString("D"));
        return await ReadAccountAsync(cmd, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<bool> TryCreateAsync(AccountRecord account, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        SqliteConnection conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable connScope = conn.ConfigureAwait(false);

        SqliteCommand cmd = new(InsertSql, conn);
        await using ConfiguredAsyncDisposable cmdScope = cmd.ConfigureAwait(false);
        _ = cmd.Parameters.AddWithValue("@id", account.Id.ToString("D"));
        _ = cmd.Parameters.AddWithValue("@name", account.Username);
        _ = cmd.Parameters.AddWithValue("@norm", account.NormalizedUsername);
        _ = cmd.Parameters.AddWithValue("@hash", PasswordHashColumn.Write(account.PasswordHash));
        _ = cmd.Parameters.AddWithValue("@salt", account.PasswordSalt);
        _ = cmd.Parameters.AddWithValue("@created", account.CreatedAtUtc.ToUnixTimeMilliseconds());
        _ = cmd.Parameters.AddWithValue("@banned", account.IsBanned ? 1L : 0L);

        int affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return affected == 1;
    }

    private static async Task<AccountRecord?> ReadAccountAsync(SqliteCommand cmd, CancellationToken ct)
    {
        SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable readerScope = reader.ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        Bytes32 hash = PasswordHashColumn.Read(await reader.GetFieldValueAsync<byte[]>(3, ct).ConfigureAwait(false));
        byte[] salt = await reader.GetFieldValueAsync<byte[]>(4, ct).ConfigureAwait(false);

        return new AccountRecord(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            hash,
            salt,
            DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(5)),
            reader.GetInt64(6) != 0);
    }
}
