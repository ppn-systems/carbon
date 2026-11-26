// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.Persistence;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;
using Carbon.Contracts.Identity;
using Microsoft.Data.Sqlite;

namespace Carbon.Infrastructure.Persistence.Sqlite;

/// <summary>
/// Pure SQLite character persistence store with parameterized SQL and transactional batch updates.
/// </summary>
public sealed class SqliteCharacterStore : ICharacterStore
{
    private const string ListSql = """
        SELECT id, account_id, name, level, appearance_preset, position_x, position_y,
               current_health, max_health, selected_hotbar_slot, inventory_blob, updated_at_utc,
               experience, current_mana, max_mana, current_stamina, max_stamina,
               class_id, character_flags, skill_flags, progression_blob,
               gold, diamonds, wallet_sequence, diamond_pity,
               needs_food, needs_malnutrition, needs_poison_seconds, needs_rest
        FROM characters
        WHERE account_id = @account_id
        ORDER BY name ASC;
        """;

    private const string FindSql = """
        SELECT id, account_id, name, level, appearance_preset, position_x, position_y,
               current_health, max_health, selected_hotbar_slot, inventory_blob, updated_at_utc,
               experience, current_mana, max_mana, current_stamina, max_stamina,
               class_id, character_flags, skill_flags, progression_blob,
               gold, diamonds, wallet_sequence, diamond_pity,
               needs_food, needs_malnutrition, needs_poison_seconds, needs_rest
        FROM characters
        WHERE id = @id
        LIMIT 1;
        """;

    private const string InsertSql = """
        INSERT INTO characters (
            id, account_id, name, level, appearance_preset, position_x, position_y,
            current_health, max_health, selected_hotbar_slot, inventory_blob, updated_at_utc,
            experience, current_mana, max_mana, current_stamina, max_stamina,
            class_id, character_flags, skill_flags, progression_blob,
            gold, diamonds, wallet_sequence, diamond_pity,
            needs_food, needs_malnutrition, needs_poison_seconds, needs_rest)
        VALUES (@id, @account_id, @name, @level, @preset, @px, @py, @hp, @max_hp, @slot, @inv, @updated, @xp, @mp, @max_mp, @stam, @max_stam, @class_id, @cflags, @sflags, @pblob, @gold, @diamonds, @wseq, @pity, @nfood, @nmal, @npoison, @nrest)
        ON CONFLICT (name) DO NOTHING;
        """;

    private const string UpdateSql = """
        UPDATE characters
        SET level = @level,
            appearance_preset = @preset,
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
            needs_rest = @nrest
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
        _ = cmd.Parameters.AddWithValue("@xp", (long)character.Experience);
        _ = cmd.Parameters.AddWithValue("@mp", character.CurrentMana);
        _ = cmd.Parameters.AddWithValue("@max_mp", character.MaxMana);
        _ = cmd.Parameters.AddWithValue("@stam", character.CurrentStamina);
        _ = cmd.Parameters.AddWithValue("@max_stam", character.MaxStamina);
        _ = cmd.Parameters.AddWithValue("@class_id", (int)character.Class);
        _ = cmd.Parameters.AddWithValue("@cflags", unchecked((long)(ulong)character.Flags));
        _ = cmd.Parameters.AddWithValue("@sflags", unchecked((long)(ulong)character.UnlockedSkills));
        _ = cmd.Parameters.AddWithValue("@pblob", character.ProgressionBlob ?? []);
        _ = cmd.Parameters.AddWithValue("@gold", character.Gold);
        _ = cmd.Parameters.AddWithValue("@diamonds", character.Diamonds);
        _ = cmd.Parameters.AddWithValue("@wseq", unchecked((long)character.WalletSequence));
        _ = cmd.Parameters.AddWithValue("@pity", character.DiamondPity);
        _ = cmd.Parameters.AddWithValue("@nfood", character.NeedsFood);
        _ = cmd.Parameters.AddWithValue("@nmal", character.NeedsMalnutrition);
        _ = cmd.Parameters.AddWithValue("@npoison", character.NeedsPoisonSeconds);
        _ = cmd.Parameters.AddWithValue("@nrest", character.NeedsRest);

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
        SqliteParameter pPreset = cmd.Parameters.Add("@preset", SqliteType.Integer);
        SqliteParameter pPx = cmd.Parameters.Add("@px", SqliteType.Real);
        SqliteParameter pPy = cmd.Parameters.Add("@py", SqliteType.Real);
        SqliteParameter pHp = cmd.Parameters.Add("@hp", SqliteType.Real);
        SqliteParameter pMaxHp = cmd.Parameters.Add("@max_hp", SqliteType.Real);
        SqliteParameter pSlot = cmd.Parameters.Add("@slot", SqliteType.Integer);
        SqliteParameter pInv = cmd.Parameters.Add("@inv", SqliteType.Blob);
        SqliteParameter pUpdated = cmd.Parameters.Add("@updated", SqliteType.Integer);
        SqliteParameter pXp = cmd.Parameters.Add("@xp", SqliteType.Integer);
        SqliteParameter pMp = cmd.Parameters.Add("@mp", SqliteType.Real);
        SqliteParameter pMaxMp = cmd.Parameters.Add("@max_mp", SqliteType.Real);
        SqliteParameter pStam = cmd.Parameters.Add("@stam", SqliteType.Real);
        SqliteParameter pMaxStam = cmd.Parameters.Add("@max_stam", SqliteType.Real);
        SqliteParameter pClassId = cmd.Parameters.Add("@class_id", SqliteType.Integer);
        SqliteParameter pCflags = cmd.Parameters.Add("@cflags", SqliteType.Integer);
        SqliteParameter pSflags = cmd.Parameters.Add("@sflags", SqliteType.Integer);
        SqliteParameter pPblob = cmd.Parameters.Add("@pblob", SqliteType.Blob);
        SqliteParameter pGold = cmd.Parameters.Add("@gold", SqliteType.Integer);
        SqliteParameter pDiamonds = cmd.Parameters.Add("@diamonds", SqliteType.Integer);
        SqliteParameter pWseq = cmd.Parameters.Add("@wseq", SqliteType.Integer);
        SqliteParameter pPity = cmd.Parameters.Add("@pity", SqliteType.Integer);
        SqliteParameter pNeedsFood = cmd.Parameters.Add("@nfood", SqliteType.Real);
        SqliteParameter pNeedsMal = cmd.Parameters.Add("@nmal", SqliteType.Real);
        SqliteParameter pNeedsPoison = cmd.Parameters.Add("@npoison", SqliteType.Real);
        SqliteParameter pNeedsRest = cmd.Parameters.Add("@nrest", SqliteType.Real);
        await cmd.PrepareAsync(ct).ConfigureAwait(false);

        for (int i = 0; i < characters.Count; i++)
        {
            CharacterRecord c = characters[i];
            pId.Value = c.Id.ToString("D");
            pLevel.Value = (int)c.Level;
            pPreset.Value = (int)c.AppearancePreset;
            pPx.Value = c.PositionX;
            pPy.Value = c.PositionY;
            pHp.Value = c.CurrentHealth;
            pMaxHp.Value = c.MaxHealth;
            pSlot.Value = (int)c.SelectedHotbarSlot;
            pInv.Value = c.InventoryBlob;
            pUpdated.Value = c.UpdatedAtUtc.ToUnixTimeMilliseconds();
            pXp.Value = (long)c.Experience;
            pMp.Value = c.CurrentMana;
            pMaxMp.Value = c.MaxMana;
            pStam.Value = c.CurrentStamina;
            pMaxStam.Value = c.MaxStamina;
            pClassId.Value = (int)c.Class;
            pCflags.Value = unchecked((long)(ulong)c.Flags);
            pSflags.Value = unchecked((long)(ulong)c.UnlockedSkills);
            pPblob.Value = c.ProgressionBlob ?? [];
            pGold.Value = c.Gold;
            pDiamonds.Value = c.Diamonds;
            pWseq.Value = unchecked((long)c.WalletSequence);
            pPity.Value = c.DiamondPity;
            pNeedsFood.Value = c.NeedsFood;
            pNeedsMal.Value = c.NeedsMalnutrition;
            pNeedsPoison.Value = c.NeedsPoisonSeconds;
            pNeedsRest.Value = c.NeedsRest;
            _ = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    private static async Task<CharacterRecord> ReadRecordAsync(SqliteDataReader reader, CancellationToken ct)
    {
        byte[] inv = await reader.GetFieldValueAsync<byte[]>(10, ct).ConfigureAwait(false);
        byte[] progBlob = await reader.IsDBNullAsync(20, ct).ConfigureAwait(false)
            ? []
            : await reader.GetFieldValueAsync<byte[]>(20, ct).ConfigureAwait(false);
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
            DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(11)),
            (uint)Math.Max(0L, reader.GetInt64(12)),
            reader.GetFloat(13),
            reader.GetFloat(14),
            reader.GetFloat(15),
            reader.GetFloat(16),
            (HeroArchetypeClass)(byte)Math.Clamp(reader.GetInt32(17), 0, 255),
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
            reader.GetFloat(28));
    }
}
