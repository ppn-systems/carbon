// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.Persistence;
using Npgsql;
using NpgsqlTypes;

namespace Carbon.Infrastructure.Persistence.Postgres;

/// <summary>
/// Pure <c>Npgsql</c> character persistence store using explicit <see cref="NpgsqlBatch"/> transactions.
/// </summary>
public sealed class PgCharacterStore : ICharacterStore
{
    private const string ListSql = """
        SELECT id, account_id, name, level, appearance_preset, position_x, position_y,
               current_health, max_health, selected_hotbar_slot, inventory_blob, updated_at_utc,
               experience, current_mana, max_mana, current_stamina, max_stamina,
               stat_vit, stat_wis, stat_pwr, stat_agi, skill1_rank, skill2_rank, skill3_rank
        FROM characters
        WHERE account_id = @account_id
        ORDER BY name ASC;
        """;

    private const string FindSql = """
        SELECT id, account_id, name, level, appearance_preset, position_x, position_y,
               current_health, max_health, selected_hotbar_slot, inventory_blob, updated_at_utc,
               experience, current_mana, max_mana, current_stamina, max_stamina,
               stat_vit, stat_wis, stat_pwr, stat_agi, skill1_rank, skill2_rank, skill3_rank
        FROM characters
        WHERE id = @id
        LIMIT 1;
        """;

    private const string InsertSql = """
        INSERT INTO characters (
            id, account_id, name, level, appearance_preset, position_x, position_y,
            current_health, max_health, selected_hotbar_slot, inventory_blob, updated_at_utc,
            experience, current_mana, max_mana, current_stamina, max_stamina,
            stat_vit, stat_wis, stat_pwr, stat_agi, skill1_rank, skill2_rank, skill3_rank)
        VALUES (@id, @account_id, @name, @level, @preset, @px, @py, @hp, @max_hp, @slot, @inv, @updated, @xp, @mp, @max_mp, @stam, @max_stam, @vit, @wis, @pwr, @agi, @s1, @s2, @s3)
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
            updated_at_utc = @updated,
            experience = @xp,
            current_mana = @mp,
            max_mana = @max_mp,
            current_stamina = @stam,
            max_stamina = @max_stam,
            stat_vit = @vit,
            stat_wis = @wis,
            stat_pwr = @pwr,
            stat_agi = @agi,
            skill1_rank = @s1,
            skill2_rank = @s2,
            skill3_rank = @s3
        WHERE id = @id;
        """;

    private readonly NpgsqlDataSource _dataSource;

    /// <summary>
    /// Initializes a new instance of the <see cref="PgCharacterStore"/> class.
    /// </summary>
    /// <param name="dataSource">PostgreSQL data source.</param>
    public PgCharacterStore(NpgsqlDataSource dataSource) =>
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CharacterRecord>> ListByAccountAsync(Guid accountId, CancellationToken ct = default)
    {
        NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable connScope = conn.ConfigureAwait(false);

        NpgsqlCommand cmd = new(ListSql, conn);
        await using ConfiguredAsyncDisposable cmdScope = cmd.ConfigureAwait(false);
        _ = cmd.Parameters.Add(new NpgsqlParameter("account_id", NpgsqlDbType.Uuid) { Value = accountId });

        List<CharacterRecord> results = [];
        DbDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
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
        NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable connScope = conn.ConfigureAwait(false);

        NpgsqlCommand cmd = new(FindSql, conn);
        await using ConfiguredAsyncDisposable cmdScope = cmd.ConfigureAwait(false);
        _ = cmd.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = characterId });

        DbDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable readerScope = reader.ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false)
            ? await ReadRecordAsync(reader, ct).ConfigureAwait(false)
            : null;
    }

    /// <inheritdoc/>
    public async Task<bool> TryCreateAsync(CharacterRecord character, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(character);

        NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable connScope = conn.ConfigureAwait(false);

        NpgsqlCommand cmd = new(InsertSql, conn);
        await using ConfiguredAsyncDisposable cmdScope = cmd.ConfigureAwait(false);
        _ = cmd.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = character.Id });
        _ = cmd.Parameters.Add(new NpgsqlParameter("account_id", NpgsqlDbType.Uuid) { Value = character.AccountId });
        _ = cmd.Parameters.Add(new NpgsqlParameter("name", NpgsqlDbType.Varchar) { Value = character.Name });
        _ = cmd.Parameters.Add(new NpgsqlParameter("level", NpgsqlDbType.Integer) { Value = (int)character.Level });
        _ = cmd.Parameters.Add(new NpgsqlParameter("preset", NpgsqlDbType.Smallint) { Value = (short)character.AppearancePreset });
        _ = cmd.Parameters.Add(new NpgsqlParameter("px", NpgsqlDbType.Real) { Value = character.PositionX });
        _ = cmd.Parameters.Add(new NpgsqlParameter("py", NpgsqlDbType.Real) { Value = character.PositionY });
        _ = cmd.Parameters.Add(new NpgsqlParameter("hp", NpgsqlDbType.Real) { Value = character.CurrentHealth });
        _ = cmd.Parameters.Add(new NpgsqlParameter("max_hp", NpgsqlDbType.Real) { Value = character.MaxHealth });
        _ = cmd.Parameters.Add(new NpgsqlParameter("slot", NpgsqlDbType.Smallint) { Value = (short)character.SelectedHotbarSlot });
        _ = cmd.Parameters.Add(new NpgsqlParameter("inv", NpgsqlDbType.Bytea) { Value = character.InventoryBlob });
        _ = cmd.Parameters.Add(new NpgsqlParameter("updated", NpgsqlDbType.TimestampTz) { Value = character.UpdatedAtUtc });
        _ = cmd.Parameters.Add(new NpgsqlParameter("xp", NpgsqlDbType.Integer) { Value = (int)character.Experience });
        _ = cmd.Parameters.Add(new NpgsqlParameter("mp", NpgsqlDbType.Real) { Value = character.CurrentMana });
        _ = cmd.Parameters.Add(new NpgsqlParameter("max_mp", NpgsqlDbType.Real) { Value = character.MaxMana });
        _ = cmd.Parameters.Add(new NpgsqlParameter("stam", NpgsqlDbType.Real) { Value = character.CurrentStamina });
        _ = cmd.Parameters.Add(new NpgsqlParameter("max_stam", NpgsqlDbType.Real) { Value = character.MaxStamina });
        _ = cmd.Parameters.Add(new NpgsqlParameter("vit", NpgsqlDbType.Integer) { Value = (int)character.VitPoints });
        _ = cmd.Parameters.Add(new NpgsqlParameter("wis", NpgsqlDbType.Integer) { Value = (int)character.WisPoints });
        _ = cmd.Parameters.Add(new NpgsqlParameter("pwr", NpgsqlDbType.Integer) { Value = (int)character.PwrPoints });
        _ = cmd.Parameters.Add(new NpgsqlParameter("agi", NpgsqlDbType.Integer) { Value = (int)character.AgiPoints });
        _ = cmd.Parameters.Add(new NpgsqlParameter("s1", NpgsqlDbType.Smallint) { Value = (short)character.Skill1Rank });
        _ = cmd.Parameters.Add(new NpgsqlParameter("s2", NpgsqlDbType.Smallint) { Value = (short)character.Skill2Rank });
        _ = cmd.Parameters.Add(new NpgsqlParameter("s3", NpgsqlDbType.Smallint) { Value = (short)character.Skill3Rank });

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

        NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable connScope = conn.ConfigureAwait(false);

        NpgsqlTransaction tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable txScope = tx.ConfigureAwait(false);

        NpgsqlBatch batch = new(conn, tx);
        await using ConfiguredAsyncDisposable batchScope = batch.ConfigureAwait(false);

        for (int i = 0; i < characters.Count; i++)
        {
            CharacterRecord c = characters[i];
            NpgsqlBatchCommand item = new(UpdateSql);
            _ = item.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = c.Id });
            _ = item.Parameters.Add(new NpgsqlParameter("level", NpgsqlDbType.Integer) { Value = (int)c.Level });
            _ = item.Parameters.Add(new NpgsqlParameter("px", NpgsqlDbType.Real) { Value = c.PositionX });
            _ = item.Parameters.Add(new NpgsqlParameter("py", NpgsqlDbType.Real) { Value = c.PositionY });
            _ = item.Parameters.Add(new NpgsqlParameter("hp", NpgsqlDbType.Real) { Value = c.CurrentHealth });
            _ = item.Parameters.Add(new NpgsqlParameter("max_hp", NpgsqlDbType.Real) { Value = c.MaxHealth });
            _ = item.Parameters.Add(new NpgsqlParameter("slot", NpgsqlDbType.Smallint) { Value = (short)c.SelectedHotbarSlot });
            _ = item.Parameters.Add(new NpgsqlParameter("inv", NpgsqlDbType.Bytea) { Value = c.InventoryBlob });
            _ = item.Parameters.Add(new NpgsqlParameter("updated", NpgsqlDbType.TimestampTz) { Value = c.UpdatedAtUtc });
            _ = item.Parameters.Add(new NpgsqlParameter("xp", NpgsqlDbType.Integer) { Value = (int)c.Experience });
            _ = item.Parameters.Add(new NpgsqlParameter("mp", NpgsqlDbType.Real) { Value = c.CurrentMana });
            _ = item.Parameters.Add(new NpgsqlParameter("max_mp", NpgsqlDbType.Real) { Value = c.MaxMana });
            _ = item.Parameters.Add(new NpgsqlParameter("stam", NpgsqlDbType.Real) { Value = c.CurrentStamina });
            _ = item.Parameters.Add(new NpgsqlParameter("max_stam", NpgsqlDbType.Real) { Value = c.MaxStamina });
            _ = item.Parameters.Add(new NpgsqlParameter("vit", NpgsqlDbType.Integer) { Value = (int)c.VitPoints });
            _ = item.Parameters.Add(new NpgsqlParameter("wis", NpgsqlDbType.Integer) { Value = (int)c.WisPoints });
            _ = item.Parameters.Add(new NpgsqlParameter("pwr", NpgsqlDbType.Integer) { Value = (int)c.PwrPoints });
            _ = item.Parameters.Add(new NpgsqlParameter("agi", NpgsqlDbType.Integer) { Value = (int)c.AgiPoints });
            _ = item.Parameters.Add(new NpgsqlParameter("s1", NpgsqlDbType.Smallint) { Value = (short)c.Skill1Rank });
            _ = item.Parameters.Add(new NpgsqlParameter("s2", NpgsqlDbType.Smallint) { Value = (short)c.Skill2Rank });
            _ = item.Parameters.Add(new NpgsqlParameter("s3", NpgsqlDbType.Smallint) { Value = (short)c.Skill3Rank });
            batch.BatchCommands.Add(item);
        }

        _ = await batch.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    private static async Task<CharacterRecord> ReadRecordAsync(DbDataReader reader, CancellationToken ct)
    {
        byte[] inv = await reader.GetFieldValueAsync<byte[]>(10, ct).ConfigureAwait(false);
        DateTimeOffset updated = await reader.GetFieldValueAsync<DateTimeOffset>(11, ct).ConfigureAwait(false);
        return new CharacterRecord(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            (ushort)reader.GetInt32(3),
            (byte)reader.GetInt16(4),
            reader.GetFloat(5),
            reader.GetFloat(6),
            reader.GetFloat(7),
            reader.GetFloat(8),
            (byte)reader.GetInt16(9),
            inv,
            updated,
            (uint)Math.Max(0, reader.GetInt32(12)),
            reader.GetFloat(13),
            reader.GetFloat(14),
            reader.GetFloat(15),
            reader.GetFloat(16),
            (ushort)Math.Max(0, reader.GetInt32(17)),
            (ushort)Math.Max(0, reader.GetInt32(18)),
            (ushort)Math.Max(0, reader.GetInt32(19)),
            (ushort)Math.Max(0, reader.GetInt32(20)),
            (byte)Math.Clamp((int)reader.GetInt16(21), 0, 5),
            (byte)Math.Clamp((int)reader.GetInt16(22), 0, 5),
            (byte)Math.Clamp((int)reader.GetInt16(23), 0, 5));
    }
}
