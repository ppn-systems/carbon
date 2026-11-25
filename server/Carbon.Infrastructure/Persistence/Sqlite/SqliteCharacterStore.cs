// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.Persistence;
using Microsoft.Data.Sqlite;

namespace Carbon.Infrastructure.Persistence.Sqlite;

/// <summary>
/// Pure SQLite character persistence store with parameterized SQL and transactional batch updates.
/// </summary>
public sealed class SqliteCharacterStore : ICharacterStore
{
    private const string ListSql = """
        SELECT id, account_id, name, level, appearance_preset, position_x, position_y,
               current_health, max_health, selected_hotbar_slot, inventory_blob, updated_at_utc
        FROM characters
        WHERE account_id = @account_id
        ORDER BY name ASC;
        """;

    private const string FindSql = """
        SELECT id, account_id, name, level, appearance_preset, position_x, position_y,
               current_health, max_health, selected_hotbar_slot, inventory_blob, updated_at_utc
        FROM characters
        WHERE id = @id
        LIMIT 1;
        """;

    private const string InsertSql = """
        INSERT INTO characters (
            id, account_id, name, level, appearance_preset, position_x, position_y,
            current_health, max_health, selected_hotbar_slot, inventory_blob, updated_at_utc)
        VALUES (@id, @account_id, @name, @level, @preset, @px, @py, @hp, @max_hp, @slot, @inv, @updated)
        ON CONFLICT (name) DO NOTHING;
        """;

    private const string UpdateSql = """
        UPDATE characters
        SET level = @level,
            position_x = @px,
            position_y = @py,
            current_health = @hp,
            max_health = @max_hp,
            selected_hotbar_slot = @slot,
            inventory_blob = @inv,
            updated_at_utc = @updated
        WHERE id = @id;
        """;

    private readonly SqliteConnectionFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteCharacterStore"/> class.
    /// </summary>
    /// <param name="factory">SQLite connection factory.</param>
    public SqliteCharacterStore(SqliteConnectionFactory factory) =>
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CharacterRecord>> ListByAccountAsync(Guid accountId, CancellationToken ct = default)
    {
        SqliteConnection conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable connScope = conn.ConfigureAwait(false);

        SqliteCommand cmd = new(ListSql, conn);
        await using ConfiguredAsyncDisposable cmdScope = cmd.ConfigureAwait(false);
        _ = cmd.Parameters.AddWithValue("@account_id", accountId.ToString("D"));

        List<CharacterRecord> results = [];
        SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable readerScope = reader.ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(await ReadRecordAsync(reader, ct).ConfigureAwait(false));
        }

        return results;
    }

    /// <inheritdoc/>
    public async Task<CharacterRecord?> FindByIdAsync(Guid characterId, CancellationToken ct = default)
    {
        SqliteConnection conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable connScope = conn.ConfigureAwait(false);

        SqliteCommand cmd = new(FindSql, conn);
        await using ConfiguredAsyncDisposable cmdScope = cmd.ConfigureAwait(false);
        _ = cmd.Parameters.AddWithValue("@id", characterId.ToString("D"));

        SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable readerScope = reader.ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false)
            ? await ReadRecordAsync(reader, ct).ConfigureAwait(false)
            : null;
    }

    /// <inheritdoc/>
    public async Task<bool> TryCreateAsync(CharacterRecord character, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(character);

        SqliteConnection conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable connScope = conn.ConfigureAwait(false);

        SqliteCommand cmd = new(InsertSql, conn);
        await using ConfiguredAsyncDisposable cmdScope = cmd.ConfigureAwait(false);
        _ = cmd.Parameters.AddWithValue("@id", character.Id.ToString("D"));
        _ = cmd.Parameters.AddWithValue("@account_id", character.AccountId.ToString("D"));
        _ = cmd.Parameters.AddWithValue("@name", character.Name);
        _ = cmd.Parameters.AddWithValue("@level", (int)character.Level);
        _ = cmd.Parameters.AddWithValue("@preset", (int)character.AppearancePreset);
        _ = cmd.Parameters.AddWithValue("@px", character.PositionX);
        _ = cmd.Parameters.AddWithValue("@py", character.PositionY);
        _ = cmd.Parameters.AddWithValue("@hp", character.CurrentHealth);
        _ = cmd.Parameters.AddWithValue("@max_hp", character.MaxHealth);
        _ = cmd.Parameters.AddWithValue("@slot", (int)character.SelectedHotbarSlot);
        _ = cmd.Parameters.AddWithValue("@inv", character.InventoryBlob);
        _ = cmd.Parameters.AddWithValue("@updated", character.UpdatedAtUtc.ToUnixTimeMilliseconds());

        int affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return affected == 1;
    }

    /// <inheritdoc/>
    public async Task SaveBatchAsync(IReadOnlyList<CharacterRecord> characters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(characters);
        if (characters.Count == 0)
        {
            return;
        }

        SqliteConnection conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable connScope = conn.ConfigureAwait(false);

        SqliteTransaction tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable txScope = tx.ConfigureAwait(false);

        SqliteCommand cmd = new(UpdateSql, conn, tx);
        await using ConfiguredAsyncDisposable cmdScope = cmd.ConfigureAwait(false);

        SqliteParameter pId = cmd.Parameters.Add("@id", SqliteType.Text);
        SqliteParameter pLevel = cmd.Parameters.Add("@level", SqliteType.Integer);
        SqliteParameter pPx = cmd.Parameters.Add("@px", SqliteType.Real);
        SqliteParameter pPy = cmd.Parameters.Add("@py", SqliteType.Real);
        SqliteParameter pHp = cmd.Parameters.Add("@hp", SqliteType.Real);
        SqliteParameter pMaxHp = cmd.Parameters.Add("@max_hp", SqliteType.Real);
        SqliteParameter pSlot = cmd.Parameters.Add("@slot", SqliteType.Integer);
        SqliteParameter pInv = cmd.Parameters.Add("@inv", SqliteType.Blob);
        SqliteParameter pUpdated = cmd.Parameters.Add("@updated", SqliteType.Integer);
        await cmd.PrepareAsync(ct).ConfigureAwait(false);

        for (int i = 0; i < characters.Count; i++)
        {
            CharacterRecord c = characters[i];
            pId.Value = c.Id.ToString("D");
            pLevel.Value = (int)c.Level;
            pPx.Value = c.PositionX;
            pPy.Value = c.PositionY;
            pHp.Value = c.CurrentHealth;
            pMaxHp.Value = c.MaxHealth;
            pSlot.Value = (int)c.SelectedHotbarSlot;
            pInv.Value = c.InventoryBlob;
            pUpdated.Value = c.UpdatedAtUtc.ToUnixTimeMilliseconds();
            _ = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    private static async Task<CharacterRecord> ReadRecordAsync(SqliteDataReader reader, CancellationToken ct)
    {
        byte[] inv = await reader.GetFieldValueAsync<byte[]>(10, ct).ConfigureAwait(false);
        return new CharacterRecord(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            reader.GetString(2),
            (ushort)reader.GetInt32(3),
            (byte)reader.GetInt32(4),
            reader.GetFloat(5),
            reader.GetFloat(6),
            reader.GetFloat(7),
            reader.GetFloat(8),
            (byte)reader.GetInt32(9),
            inv,
            DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(11)));
    }
}
