// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.Persistence;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;
using Carbon.Contracts.Identity;
using Carbon.Items.Races;
using Npgsql;
using NpgsqlTypes;

namespace Carbon.Infrastructure.Persistence.Postgres;

/// <summary>
/// Pure <c>Npgsql</c> character persistence store using explicit <see cref="NpgsqlBatch"/> transactions.
/// </summary>
public sealed class PgCharacterStore : ICharacterStore
{
    private const string ListSql = """
        SELECT id, account_id, name, level, appearance, position_x, position_y,
               current_health, max_health, selected_hotbar_slot, inventory_blob, updated_at_utc,
               experience, current_mana, max_mana, current_stamina, max_stamina,
               class_id, character_flags, skill_flags, progression_blob,
               gold, diamonds, wallet_sequence, diamond_pity,
               needs_food, needs_malnutrition, needs_poison_seconds, needs_rest, status_effects
        FROM characters
        WHERE account_id = @account_id
        ORDER BY name ASC;
        """;

    private const string FindSql = """
        SELECT id, account_id, name, level, appearance, position_x, position_y,
               current_health, max_health, selected_hotbar_slot, inventory_blob, updated_at_utc,
               experience, current_mana, max_mana, current_stamina, max_stamina,
               class_id, character_flags, skill_flags, progression_blob,
               gold, diamonds, wallet_sequence, diamond_pity,
               needs_food, needs_malnutrition, needs_poison_seconds, needs_rest, status_effects
        FROM characters
        WHERE id = @id
        LIMIT 1;
        """;

    private const string InsertSql = """
        INSERT INTO characters (
            id, account_id, name, level, appearance, position_x, position_y,
            current_health, max_health, selected_hotbar_slot, inventory_blob, updated_at_utc,
            experience, current_mana, max_mana, current_stamina, max_stamina,
            class_id, character_flags, skill_flags, progression_blob,
            gold, diamonds, wallet_sequence, diamond_pity,
            needs_food, needs_malnutrition, needs_poison_seconds, needs_rest, status_effects)
        VALUES (@id, @account_id, @name, @level, @appearance, @px, @py, @hp, @max_hp, @slot, @inv, @updated, @xp, @mp, @max_mp, @stam, @max_stam, @class_id, @cflags, @sflags, @pblob, @gold, @diamonds, @wseq, @pity, @nfood, @nmal, @npoison, @nrest, @nstatus)
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
            class_id = @class_id,
            character_flags = @cflags,
            skill_flags = @sflags,
            progression_blob = @pblob,
            gold = @gold,
            diamonds = @diamonds,
            wallet_sequence = @wseq,
            diamond_pity = @pity,
            needs_food = @nfood,
            needs_malnutrition = @nmal,
            needs_poison_seconds = @npoison,
            needs_rest = @nrest,
            status_effects = @nstatus
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
        _ = cmd.Parameters.Add(new NpgsqlParameter("appearance", NpgsqlDbType.Bytea) { Value = CharacterLookBinaryCodec.Serialize(character.Look) });
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
        _ = cmd.Parameters.Add(new NpgsqlParameter("class_id", NpgsqlDbType.Smallint) { Value = (short)character.Class });
        _ = cmd.Parameters.Add(new NpgsqlParameter("cflags", NpgsqlDbType.Bigint) { Value = unchecked((long)(ulong)character.Flags) });
        _ = cmd.Parameters.Add(new NpgsqlParameter("sflags", NpgsqlDbType.Bigint) { Value = unchecked((long)(ulong)character.UnlockedSkills) });
        _ = cmd.Parameters.Add(new NpgsqlParameter("pblob", NpgsqlDbType.Bytea) { Value = character.ProgressionBlob ?? [] });
        _ = cmd.Parameters.Add(new NpgsqlParameter("gold", NpgsqlDbType.Bigint) { Value = character.Gold });
        _ = cmd.Parameters.Add(new NpgsqlParameter("diamonds", NpgsqlDbType.Bigint) { Value = character.Diamonds });
        _ = cmd.Parameters.Add(new NpgsqlParameter("wseq", NpgsqlDbType.Bigint) { Value = unchecked((long)character.WalletSequence) });
        _ = cmd.Parameters.Add(new NpgsqlParameter("pity", NpgsqlDbType.Integer) { Value = character.DiamondPity });
        _ = cmd.Parameters.Add(new NpgsqlParameter("nfood", NpgsqlDbType.Real) { Value = character.NeedsFood });
        _ = cmd.Parameters.Add(new NpgsqlParameter("nmal", NpgsqlDbType.Real) { Value = character.NeedsMalnutrition });
        _ = cmd.Parameters.Add(new NpgsqlParameter("npoison", NpgsqlDbType.Real) { Value = character.NeedsPoisonSeconds });
        _ = cmd.Parameters.Add(new NpgsqlParameter("nrest", NpgsqlDbType.Real) { Value = character.NeedsRest });
        _ = cmd.Parameters.Add(new NpgsqlParameter("nstatus", NpgsqlDbType.Text) { Value = character.StatusEffects ?? string.Empty });

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
            _ = item.Parameters.Add(new NpgsqlParameter("class_id", NpgsqlDbType.Smallint) { Value = (short)c.Class });
            _ = item.Parameters.Add(new NpgsqlParameter("cflags", NpgsqlDbType.Bigint) { Value = unchecked((long)(ulong)c.Flags) });
            _ = item.Parameters.Add(new NpgsqlParameter("sflags", NpgsqlDbType.Bigint) { Value = unchecked((long)(ulong)c.UnlockedSkills) });
            _ = item.Parameters.Add(new NpgsqlParameter("pblob", NpgsqlDbType.Bytea) { Value = c.ProgressionBlob ?? [] });
            _ = item.Parameters.Add(new NpgsqlParameter("gold", NpgsqlDbType.Bigint) { Value = c.Gold });
            _ = item.Parameters.Add(new NpgsqlParameter("diamonds", NpgsqlDbType.Bigint) { Value = c.Diamonds });
            _ = item.Parameters.Add(new NpgsqlParameter("wseq", NpgsqlDbType.Bigint) { Value = unchecked((long)c.WalletSequence) });
            _ = item.Parameters.Add(new NpgsqlParameter("pity", NpgsqlDbType.Integer) { Value = c.DiamondPity });
            _ = item.Parameters.Add(new NpgsqlParameter("nfood", NpgsqlDbType.Real) { Value = c.NeedsFood });
            _ = item.Parameters.Add(new NpgsqlParameter("nmal", NpgsqlDbType.Real) { Value = c.NeedsMalnutrition });
            _ = item.Parameters.Add(new NpgsqlParameter("npoison", NpgsqlDbType.Real) { Value = c.NeedsPoisonSeconds });
            _ = item.Parameters.Add(new NpgsqlParameter("nrest", NpgsqlDbType.Real) { Value = c.NeedsRest });
            _ = item.Parameters.Add(new NpgsqlParameter("nstatus", NpgsqlDbType.Text) { Value = c.StatusEffects ?? string.Empty });
            batch.BatchCommands.Add(item);
        }

        _ = await batch.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    private static async Task<CharacterRecord> ReadRecordAsync(DbDataReader reader, CancellationToken ct)
    {
        byte[] inv = await reader.GetFieldValueAsync<byte[]>(10, ct).ConfigureAwait(false);
        DateTimeOffset updated = await reader.GetFieldValueAsync<DateTimeOffset>(11, ct).ConfigureAwait(false);
        byte[] progBlob = await reader.IsDBNullAsync(20, ct).ConfigureAwait(false)
            ? []
            : await reader.GetFieldValueAsync<byte[]>(20, ct).ConfigureAwait(false);
        return new CharacterRecord(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            (ushort)reader.GetInt32(3),
            CharacterLookBinaryCodec.Deserialize(await reader.GetFieldValueAsync<byte[]>(4, ct).ConfigureAwait(false)),
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
            (HeroArchetypeClass)(byte)Math.Clamp((int)reader.GetInt16(17), 0, 255),
            (CharacterTraits)unchecked((ulong)reader.GetInt64(18)),
            (CharacterSkillMask)unchecked((ulong)reader.GetInt64(19)),
            progBlob,
            reader.GetInt64(21),
            reader.GetInt64(22),
            unchecked((ulong)reader.GetInt64(23)),
            reader.GetInt32(24),
            reader.GetFloat(25),
            reader.GetFloat(26),
            reader.GetFloat(27),
            reader.GetFloat(28),
            reader.GetString(29));
    }
}
