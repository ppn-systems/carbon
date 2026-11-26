// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.Persistence;
using Microsoft.Data.Sqlite;

namespace Carbon.Infrastructure.Persistence.Sqlite;

/// <summary>
/// Idempotent pure-SQLite schema migrator using <c>Microsoft.Data.Sqlite</c> with zero reflection.
/// </summary>
public sealed class SqliteSchemaMigrator : IDatabaseMigrator
{
    private const string EnsureMigrationsTableSql = """
        CREATE TABLE IF NOT EXISTS schema_migrations (
            id          TEXT PRIMARY KEY,
            applied_utc TEXT NOT NULL
        );
        """;

    private const string InitialSchemaSql = """
        CREATE TABLE IF NOT EXISTS accounts (
            id                  TEXT    PRIMARY KEY,
            username            TEXT    NOT NULL,
            normalized_username TEXT    NOT NULL UNIQUE,
            password_hash       BLOB    NOT NULL,
            password_salt       BLOB    NOT NULL,
            created_at_utc      INTEGER NOT NULL,
            is_banned           INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS characters (
            id                   TEXT    PRIMARY KEY,
            account_id           TEXT    NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
            name                 TEXT    NOT NULL UNIQUE,
            level                INTEGER NOT NULL DEFAULT 1,
            appearance_preset    INTEGER NOT NULL DEFAULT 0,
            position_x           REAL    NOT NULL,
            position_y           REAL    NOT NULL,
            current_health       REAL    NOT NULL,
            max_health           REAL    NOT NULL,
            selected_hotbar_slot INTEGER NOT NULL DEFAULT 0,
            inventory_blob       BLOB    NOT NULL,
            updated_at_utc       INTEGER NOT NULL,
            experience           INTEGER NOT NULL DEFAULT 0,
            current_mana         REAL    NOT NULL DEFAULT 80.0,
            max_mana             REAL    NOT NULL DEFAULT 80.0,
            current_stamina      REAL    NOT NULL DEFAULT 100.0,
            max_stamina          REAL    NOT NULL DEFAULT 100.0,
            class_id             INTEGER NOT NULL DEFAULT 0,
            character_flags      INTEGER NOT NULL DEFAULT 1,
            skill_flags          INTEGER NOT NULL DEFAULT 0,
            progression_blob     BLOB    NOT NULL DEFAULT X''
        );

        CREATE INDEX IF NOT EXISTS ix_characters_account_id ON characters(account_id);
        CREATE UNIQUE INDEX IF NOT EXISTS ux_characters_lower_name ON characters(LOWER(name));

        CREATE TABLE IF NOT EXISTS world_chunk_deltas (
            chunk_x           INTEGER NOT NULL,
            chunk_y           INTEGER NOT NULL,
            revision          INTEGER NOT NULL,
            generator_version INTEGER NOT NULL,
            payload           BLOB    NOT NULL,
            updated_at_utc    INTEGER NOT NULL,
            PRIMARY KEY (chunk_x, chunk_y)
        );
        """;

    private readonly SqliteConnectionFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteSchemaMigrator"/> class.
    /// </summary>
    /// <param name="factory">SQLite connection factory.</param>
    public SqliteSchemaMigrator(SqliteConnectionFactory factory) =>
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <inheritdoc/>
    public async Task MigrateAsync(CancellationToken ct = default)
    {
        SqliteConnection conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable connScope = conn.ConfigureAwait(false);

        SqliteTransaction tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable txScope = tx.ConfigureAwait(false);

        SqliteCommand ensure = new(EnsureMigrationsTableSql, conn, tx);
        await using (ConfiguredAsyncDisposable ensureScope = ensure.ConfigureAwait(false))
        {
            _ = await ensure.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        bool exists;
        SqliteCommand check = new("SELECT 1 FROM schema_migrations WHERE id = @id LIMIT 1;", conn, tx);
        await using (ConfiguredAsyncDisposable checkScope = check.ConfigureAwait(false))
        {
            _ = check.Parameters.AddWithValue("@id", "0001_initial");
            object? scalar = await check.ExecuteScalarAsync(ct).ConfigureAwait(false);
            exists = scalar is not null && scalar != DBNull.Value;
        }

        if (!exists)
        {
            SqliteCommand ddl = new(InitialSchemaSql, conn, tx);
            await using (ConfiguredAsyncDisposable ddlScope = ddl.ConfigureAwait(false))
            {
                _ = await ddl.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            SqliteCommand insert = new("INSERT INTO schema_migrations (id, applied_utc) VALUES (@id, @utc);", conn, tx);
            await using ConfiguredAsyncDisposable insertScope = insert.ConfigureAwait(false);
            _ = insert.Parameters.AddWithValue("@id", "0001_initial");
            _ = insert.Parameters.AddWithValue("@utc", DateTimeOffset.UtcNow.ToString("O"));
            _ = await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        bool hasRpgMigration;
        SqliteCommand checkRpg = new("SELECT 1 FROM schema_migrations WHERE id = @id LIMIT 1;", conn, tx);
        await using (ConfiguredAsyncDisposable checkRpgScope = checkRpg.ConfigureAwait(false))
        {
            _ = checkRpg.Parameters.AddWithValue("@id", "0002_rpg_progression");
            object? scalar = await checkRpg.ExecuteScalarAsync(ct).ConfigureAwait(false);
            hasRpgMigration = scalar is not null && scalar != DBNull.Value;
        }

        if (!hasRpgMigration)
        {
            System.Collections.Generic.HashSet<string> existingCols = new(StringComparer.OrdinalIgnoreCase);
            SqliteCommand pragma = new("PRAGMA table_info(characters);", conn, tx);
            await using (ConfiguredAsyncDisposable pragmaScope = pragma.ConfigureAwait(false))
            {
                SqliteDataReader colReader = await pragma.ExecuteReaderAsync(ct).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable colReaderScope = colReader.ConfigureAwait(false);
                while (await colReader.ReadAsync(ct).ConfigureAwait(false))
                {
                    _ = existingCols.Add(colReader.GetString(1));
                }
            }

            (string Col, string AlterSql)[] migrations =
            [
                ("experience", "ALTER TABLE characters ADD COLUMN experience INTEGER NOT NULL DEFAULT 0;"),
                ("current_mana", "ALTER TABLE characters ADD COLUMN current_mana REAL NOT NULL DEFAULT 80.0;"),
                ("max_mana", "ALTER TABLE characters ADD COLUMN max_mana REAL NOT NULL DEFAULT 80.0;"),
                ("current_stamina", "ALTER TABLE characters ADD COLUMN current_stamina REAL NOT NULL DEFAULT 100.0;"),
                ("max_stamina", "ALTER TABLE characters ADD COLUMN max_stamina REAL NOT NULL DEFAULT 100.0;"),
            ];

            for (int i = 0; i < migrations.Length; i++)
            {
                if (!existingCols.Contains(migrations[i].Col))
                {
                    SqliteCommand alter = new(migrations[i].AlterSql, conn, tx);
                    await using ConfiguredAsyncDisposable alterScope = alter.ConfigureAwait(false);
                    _ = await alter.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
            }

            SqliteCommand insertRpg = new("INSERT INTO schema_migrations (id, applied_utc) VALUES (@id, @utc);", conn, tx);
            await using ConfiguredAsyncDisposable insertRpgScope = insertRpg.ConfigureAwait(false);
            _ = insertRpg.Parameters.AddWithValue("@id", "0002_rpg_progression");
            _ = insertRpg.Parameters.AddWithValue("@utc", DateTimeOffset.UtcNow.ToString("O"));
            _ = await insertRpg.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        bool hasPointsMigration;
        SqliteCommand checkPts = new("SELECT 1 FROM schema_migrations WHERE id = @id LIMIT 1;", conn, tx);
        await using (ConfiguredAsyncDisposable checkPtsScope = checkPts.ConfigureAwait(false))
        {
            _ = checkPts.Parameters.AddWithValue("@id", "0003_stat_and_skill_points");
            object? scalar = await checkPts.ExecuteScalarAsync(ct).ConfigureAwait(false);
            hasPointsMigration = scalar is not null && scalar != DBNull.Value;
        }

        if (!hasPointsMigration)
        {
            System.Collections.Generic.HashSet<string> existingCols = new(StringComparer.OrdinalIgnoreCase);
            SqliteCommand pragma = new("PRAGMA table_info(characters);", conn, tx);
            await using (ConfiguredAsyncDisposable pragmaScope = pragma.ConfigureAwait(false))
            {
                SqliteDataReader colReader = await pragma.ExecuteReaderAsync(ct).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable colReaderScope = colReader.ConfigureAwait(false);
                while (await colReader.ReadAsync(ct).ConfigureAwait(false))
                {
                    _ = existingCols.Add(colReader.GetString(1));
                }
            }

            (string Col, string AlterSql)[] pointMigrations =
            [
                ("stat_vit", "ALTER TABLE characters ADD COLUMN stat_vit INTEGER NOT NULL DEFAULT 0;"),
                ("stat_wis", "ALTER TABLE characters ADD COLUMN stat_wis INTEGER NOT NULL DEFAULT 0;"),
                ("stat_pwr", "ALTER TABLE characters ADD COLUMN stat_pwr INTEGER NOT NULL DEFAULT 0;"),
                ("stat_agi", "ALTER TABLE characters ADD COLUMN stat_agi INTEGER NOT NULL DEFAULT 0;"),
                ("skill1_rank", "ALTER TABLE characters ADD COLUMN skill1_rank INTEGER NOT NULL DEFAULT 0;"),
                ("skill2_rank", "ALTER TABLE characters ADD COLUMN skill2_rank INTEGER NOT NULL DEFAULT 0;"),
                ("skill3_rank", "ALTER TABLE characters ADD COLUMN skill3_rank INTEGER NOT NULL DEFAULT 0;"),
            ];

            for (int i = 0; i < pointMigrations.Length; i++)
            {
                if (!existingCols.Contains(pointMigrations[i].Col))
                {
                    SqliteCommand alter = new(pointMigrations[i].AlterSql, conn, tx);
                    await using ConfiguredAsyncDisposable alterScope = alter.ConfigureAwait(false);
                    _ = await alter.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
            }

            SqliteCommand insertPts = new("INSERT INTO schema_migrations (id, applied_utc) VALUES (@id, @utc);", conn, tx);
            await using ConfiguredAsyncDisposable insertPtsScope = insertPts.ConfigureAwait(false);
            _ = insertPts.Parameters.AddWithValue("@id", "0003_stat_and_skill_points");
            _ = insertPts.Parameters.AddWithValue("@utc", DateTimeOffset.UtcNow.ToString("O"));
            _ = await insertPts.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        bool hasExtMigration;
        SqliteCommand checkExt = new("SELECT 1 FROM schema_migrations WHERE id = @id LIMIT 1;", conn, tx);
        await using (ConfiguredAsyncDisposable checkExtScope = checkExt.ConfigureAwait(false))
        {
            _ = checkExt.Parameters.AddWithValue("@id", "0004_character_class_flags_progression_blob");
            object? scalar = await checkExt.ExecuteScalarAsync(ct).ConfigureAwait(false);
            hasExtMigration = scalar is not null && scalar != DBNull.Value;
        }

        if (!hasExtMigration)
        {
            HashSet<string> existingCols = new(StringComparer.OrdinalIgnoreCase);
            SqliteCommand pragma = new("PRAGMA table_info(characters);", conn, tx);
            await using (ConfiguredAsyncDisposable pragmaScope = pragma.ConfigureAwait(false))
            {
                SqliteDataReader colReader = await pragma.ExecuteReaderAsync(ct).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable colReaderScope = colReader.ConfigureAwait(false);
                while (await colReader.ReadAsync(ct).ConfigureAwait(false))
                {
                    _ = existingCols.Add(colReader.GetString(1));
                }
            }

            (string Col, string AlterSql)[] extMigrations =
            [
                ("class_id", "ALTER TABLE characters ADD COLUMN class_id INTEGER NOT NULL DEFAULT 0;"),
                ("character_flags", "ALTER TABLE characters ADD COLUMN character_flags INTEGER NOT NULL DEFAULT 1;"),
                ("skill_flags", "ALTER TABLE characters ADD COLUMN skill_flags INTEGER NOT NULL DEFAULT 0;"),
                ("progression_blob", "ALTER TABLE characters ADD COLUMN progression_blob BLOB NOT NULL DEFAULT X'';"),
            ];

            for (int i = 0; i < extMigrations.Length; i++)
            {
                if (!existingCols.Contains(extMigrations[i].Col))
                {
                    SqliteCommand alter = new(extMigrations[i].AlterSql, conn, tx);
                    await using ConfiguredAsyncDisposable alterScope = alter.ConfigureAwait(false);
                    _ = await alter.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
            }

            SqliteCommand idxCmd = new("CREATE UNIQUE INDEX IF NOT EXISTS ux_characters_lower_name ON characters(LOWER(name));", conn, tx);
            await using (ConfiguredAsyncDisposable idxScope = idxCmd.ConfigureAwait(false))
            {
                _ = await idxCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            if (existingCols.Contains("stat_vit") && existingCols.Contains("skill1_rank"))
            {
                List<(string Id, long Flags, long SkillFlags, byte[] Blob)> updates = [];
                SqliteCommand selectLegacy = new(
                    "SELECT id, length(inventory_blob), stat_vit, stat_wis, stat_pwr, stat_agi, skill1_rank, skill2_rank, skill3_rank FROM characters WHERE length(progression_blob) = 0;",
                    conn,
                    tx);
                await using (ConfiguredAsyncDisposable selectScope = selectLegacy.ConfigureAwait(false))
                {
                    SqliteDataReader r = await selectLegacy.ExecuteReaderAsync(ct).ConfigureAwait(false);
                    await using ConfiguredAsyncDisposable rScope = r.ConfigureAwait(false);
                    while (await r.ReadAsync(ct).ConfigureAwait(false))
                    {
                        string id = r.GetString(0);
                        int invLen = await r.IsDBNullAsync(1, ct).ConfigureAwait(false) ? 0 : r.GetInt32(1);
                        ushort vit = (ushort)Math.Max(0, r.GetInt32(2));
                        ushort wis = (ushort)Math.Max(0, r.GetInt32(3));
                        ushort pwr = (ushort)Math.Max(0, r.GetInt32(4));
                        ushort agi = (ushort)Math.Max(0, r.GetInt32(5));
                        byte s1 = (byte)Math.Clamp(r.GetInt32(6), 0, 5);
                        byte s2 = (byte)Math.Clamp(r.GetInt32(7), 0, 5);
                        byte s3 = (byte)Math.Clamp(r.GetInt32(8), 0, 5);

                        byte[] blob = PackLegacyProgressionBlob(vit, wis, pwr, agi, s1, s2, s3, out long skillMask);
                        long charFlags = invLen > 0 ? 1L : 0L;
                        updates.Add((id, charFlags, skillMask, blob));
                    }
                }

                for (int i = 0; i < updates.Count; i++)
                {
                    (string id, long charFlags, long skillMask, byte[] blob) = updates[i];
                    SqliteCommand upd = new(
                        "UPDATE characters SET character_flags = @cf, skill_flags = @sf, progression_blob = @pb WHERE id = @id;",
                        conn,
                        tx);
                    await using ConfiguredAsyncDisposable updScope = upd.ConfigureAwait(false);
                    _ = upd.Parameters.AddWithValue("@cf", charFlags);
                    _ = upd.Parameters.AddWithValue("@sf", skillMask);
                    _ = upd.Parameters.AddWithValue("@pb", blob);
                    _ = upd.Parameters.AddWithValue("@id", id);
                    _ = await upd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
            }

            SqliteCommand insertExt = new("INSERT INTO schema_migrations (id, applied_utc) VALUES (@id, @utc);", conn, tx);
            await using ConfiguredAsyncDisposable insertExtScope = insertExt.ConfigureAwait(false);
            _ = insertExt.Parameters.AddWithValue("@id", "0004_character_class_flags_progression_blob");
            _ = insertExt.Parameters.AddWithValue("@utc", DateTimeOffset.UtcNow.ToString("O"));
            _ = await insertExt.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        // ADR-012: server-authoritative wallet. Later additive column migrations follow the same pattern.
        await AddCharacterColumnsAsync(
            conn,
            tx,
            "0005_character_wallet",
            [
                ("gold", "INTEGER NOT NULL DEFAULT 0"),
                ("diamonds", "INTEGER NOT NULL DEFAULT 0"),
                ("wallet_sequence", "INTEGER NOT NULL DEFAULT 0"),
                ("diamond_pity", "INTEGER NOT NULL DEFAULT 0"),
            ],
            ct).ConfigureAwait(false);

        // ADR-015: survival needs. needs_food = -1 marks "never saved" so existing characters spawn fully fed.
        await AddCharacterColumnsAsync(
            conn,
            tx,
            "0006_character_needs",
            [
                ("needs_food", "REAL NOT NULL DEFAULT -1"),
                ("needs_malnutrition", "REAL NOT NULL DEFAULT 0"),
                ("needs_poison_seconds", "REAL NOT NULL DEFAULT 0"),
            ],
            ct).ConfigureAwait(false);

        // ADR-017: rest need. needs_rest = -1 marks "never saved" so existing characters spawn fully rested.
        await AddCharacterColumnsAsync(
            conn,
            tx,
            "0007_character_rest",
            [
                ("needs_rest", "REAL NOT NULL DEFAULT -1"),
            ],
            ct).ConfigureAwait(false);

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies an additive, idempotent migration that adds missing columns to <c>characters</c> and records
    /// <paramref name="migrationId"/> in <c>schema_migrations</c>. Column names and definitions are compile-time constants.
    /// </summary>
    private static async Task AddCharacterColumnsAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        string migrationId,
        (string Column, string Definition)[] columns,
        CancellationToken ct)
    {
        SqliteCommand check = new("SELECT 1 FROM schema_migrations WHERE id = @id LIMIT 1;", conn, tx);
        await using (ConfiguredAsyncDisposable checkScope = check.ConfigureAwait(false))
        {
            _ = check.Parameters.AddWithValue("@id", migrationId);
            object? scalar = await check.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (scalar is not null && scalar != DBNull.Value)
            {
                return;
            }
        }

        HashSet<string> existing = new(StringComparer.OrdinalIgnoreCase);
        SqliteCommand pragma = new("PRAGMA table_info(characters);", conn, tx);
        await using (ConfiguredAsyncDisposable pragmaScope = pragma.ConfigureAwait(false))
        {
            SqliteDataReader reader = await pragma.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable readerScope = reader.ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                _ = existing.Add(reader.GetString(1));
            }
        }

        for (int i = 0; i < columns.Length; i++)
        {
            if (existing.Contains(columns[i].Column))
            {
                continue;
            }

#pragma warning disable CA2100 // Column names/definitions are compile-time constants, never user input.
            SqliteCommand alter = new($"ALTER TABLE characters ADD COLUMN {columns[i].Column} {columns[i].Definition};", conn, tx);
#pragma warning restore CA2100
            await using ConfiguredAsyncDisposable alterScope = alter.ConfigureAwait(false);
            _ = await alter.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        SqliteCommand mark = new("INSERT INTO schema_migrations (id, applied_utc) VALUES (@id, @utc);", conn, tx);
        await using ConfiguredAsyncDisposable markScope = mark.ConfigureAwait(false);
        _ = mark.Parameters.AddWithValue("@id", migrationId);
        _ = mark.Parameters.AddWithValue("@utc", DateTimeOffset.UtcNow.ToString("O"));
        _ = await mark.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static byte[] PackLegacyProgressionBlob(
        ushort vit,
        ushort wis,
        ushort pwr,
        ushort agi,
        byte s1,
        byte s2,
        byte s3,
        out long skillMask)
    {
        ulong mask = 0UL;
        if (s1 > 0)
        {
            mask |= 1UL << 0;
        }

        if (s2 > 0)
        {
            mask |= 1UL << 1;
        }

        if (s3 > 0)
        {
            mask |= 1UL << 2;
        }

        skillMask = unchecked((long)mask);

        if (vit == 0 && wis == 0 && pwr == 0 && agi == 0 && mask == 0UL)
        {
            return [];
        }

        List<(byte Attr, ushort Pts)> attrs = [];
        if (vit > 0)
        {
            attrs.Add((0, vit));
        }

        if (wis > 0)
        {
            attrs.Add((1, wis));
        }

        if (pwr > 0)
        {
            attrs.Add((2, pwr));
        }

        if (agi > 0)
        {
            attrs.Add((3, agi));
        }

        List<(ushort SkillId, byte Rank)> skills = [];
        if (s1 > 0)
        {
            skills.Add((1, s1));
        }

        if (s2 > 0)
        {
            skills.Add((2, s2));
        }

        if (s3 > 0)
        {
            skills.Add((3, s3));
        }

        byte[] buf = new byte[12 + (attrs.Count * 3) + (skills.Count * 3)];
        Span<byte> span = buf;
        span[0] = 1;
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(1, 8), mask);
        span[9] = (byte)attrs.Count;
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(10, 2), (ushort)skills.Count);

        int offset = 12;
        for (int i = 0; i < attrs.Count; i++)
        {
            span[offset] = attrs[i].Attr;
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(offset + 1, 2), attrs[i].Pts);
            offset += 3;
        }

        for (int i = 0; i < skills.Count; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(offset, 2), skills[i].SkillId);
            span[offset + 2] = skills[i].Rank;
            offset += 3;
        }

        return buf;
    }
}
