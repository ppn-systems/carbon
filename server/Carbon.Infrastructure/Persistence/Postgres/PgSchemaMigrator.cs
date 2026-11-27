// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.Persistence;
using Npgsql;

namespace Carbon.Infrastructure.Persistence.Postgres;

/// <summary>
/// Idempotent pure-SQL schema migrator for PostgreSQL protected by a transaction advisory lock.
/// </summary>
public sealed class PgSchemaMigrator : IDatabaseMigrator
{
    private const long MigrationAdvisoryLockId = 0x434152424F4E4442L;

    private const string EnsureMigrationsTableSql = """
        CREATE TABLE IF NOT EXISTS schema_migrations (
            id          TEXT        PRIMARY KEY,
            applied_utc TIMESTAMPTZ NOT NULL DEFAULT now()
        );
        """;

    private const string InitialSchemaSql = """
        CREATE TABLE IF NOT EXISTS accounts (
            id                  UUID        PRIMARY KEY,
            username            VARCHAR(32) NOT NULL,
            normalized_username VARCHAR(32) NOT NULL UNIQUE,
            password_hash       BYTEA       NOT NULL,
            password_salt       BYTEA       NOT NULL,
            created_at_utc      TIMESTAMPTZ NOT NULL,
            is_banned           BOOLEAN     NOT NULL DEFAULT FALSE
        );

        CREATE TABLE IF NOT EXISTS characters (
            id                   UUID        PRIMARY KEY,
            account_id           UUID        NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
            name                 VARCHAR(32) NOT NULL UNIQUE,
            level                INTEGER     NOT NULL DEFAULT 1,
            appearance_preset    SMALLINT    NOT NULL DEFAULT 0,
            position_x           REAL        NOT NULL,
            position_y           REAL        NOT NULL,
            current_health       REAL        NOT NULL,
            max_health           REAL        NOT NULL,
            selected_hotbar_slot SMALLINT    NOT NULL DEFAULT 0,
            inventory_blob       BYTEA       NOT NULL,
            updated_at_utc       TIMESTAMPTZ NOT NULL,
            experience           INTEGER     NOT NULL DEFAULT 0,
            current_mana         REAL        NOT NULL DEFAULT 80.0,
            max_mana             REAL        NOT NULL DEFAULT 80.0,
            current_stamina      REAL        NOT NULL DEFAULT 100.0,
            max_stamina          REAL        NOT NULL DEFAULT 100.0,
            class_id             SMALLINT    NOT NULL DEFAULT 0,
            character_flags      BIGINT      NOT NULL DEFAULT 1,
            skill_flags          BIGINT      NOT NULL DEFAULT 0,
            progression_blob     BYTEA       NOT NULL DEFAULT ''::bytea
        );

        CREATE INDEX IF NOT EXISTS ix_characters_account_id ON characters(account_id);
        CREATE UNIQUE INDEX IF NOT EXISTS ux_characters_lower_name ON characters(LOWER(name));

        CREATE TABLE IF NOT EXISTS world_chunk_deltas (
            chunk_x           INTEGER     NOT NULL,
            chunk_y           INTEGER     NOT NULL,
            revision          BIGINT      NOT NULL,
            generator_version INTEGER     NOT NULL,
            payload           BYTEA       NOT NULL,
            updated_at_utc    TIMESTAMPTZ NOT NULL,
            PRIMARY KEY (chunk_x, chunk_y)
        );
        """;

    // ADR-025: trader sales per restock epoch (stock is derived from the gameplay secret and the epoch). A separate
    // migration, not part of 0001_initial, so databases created before villages also get the table.
    private const string TraderStockSql = """
        CREATE TABLE IF NOT EXISTS trader_stock (
            trader_key        BIGINT      NOT NULL PRIMARY KEY,
            epoch             BIGINT      NOT NULL,
            sold              BYTEA       NOT NULL,
            updated_at_utc    TIMESTAMPTZ NOT NULL
        );
        """;

    // ADR-026: the world's gameplay secret (one row). Server-only: never logged, never sent to clients.
    private const string GameplaySecretSql = """
        CREATE TABLE IF NOT EXISTS gameplay_secret (
            id                SMALLINT    NOT NULL PRIMARY KEY CHECK (id = 1),
            secret            BYTEA       NOT NULL,
            created_at_utc    TIMESTAMPTZ NOT NULL
        );
        """;

    private readonly NpgsqlDataSource _dataSource;

    /// <summary>
    /// Initializes a new instance of the <see cref="PgSchemaMigrator"/> class.
    /// </summary>
    /// <param name="dataSource">PostgreSQL data source.</param>
    public PgSchemaMigrator(NpgsqlDataSource dataSource) =>
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));

    /// <inheritdoc/>
    public async Task MigrateAsync(CancellationToken ct = default)
    {
        NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable connScope = conn.ConfigureAwait(false);

        NpgsqlTransaction tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable txScope = tx.ConfigureAwait(false);

        NpgsqlCommand lockCmd = new("SELECT pg_advisory_xact_lock(@lock_id);", conn, tx);
        await using (ConfiguredAsyncDisposable lockScope = lockCmd.ConfigureAwait(false))
        {
            _ = lockCmd.Parameters.AddWithValue("lock_id", MigrationAdvisoryLockId);
            _ = await lockCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        NpgsqlCommand ensureCmd = new(EnsureMigrationsTableSql, conn, tx);
        await using (ConfiguredAsyncDisposable ensureScope = ensureCmd.ConfigureAwait(false))
        {
            _ = await ensureCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        bool alreadyApplied;
        NpgsqlCommand checkCmd = new("SELECT EXISTS (SELECT 1 FROM schema_migrations WHERE id = @id);", conn, tx);
        await using (ConfiguredAsyncDisposable checkScope = checkCmd.ConfigureAwait(false))
        {
            _ = checkCmd.Parameters.AddWithValue("id", "0001_initial");
            object? scalar = await checkCmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            alreadyApplied = scalar is true;
        }

        if (!alreadyApplied)
        {
            NpgsqlCommand schemaCmd = new(InitialSchemaSql, conn, tx);
            await using (ConfiguredAsyncDisposable schemaScope = schemaCmd.ConfigureAwait(false))
            {
                _ = await schemaCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            NpgsqlCommand markCmd = new("INSERT INTO schema_migrations (id) VALUES (@id);", conn, tx);
            await using ConfiguredAsyncDisposable markScope = markCmd.ConfigureAwait(false);
            _ = markCmd.Parameters.AddWithValue("id", "0001_initial");
            _ = await markCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        bool hasRpgMigration;
        NpgsqlCommand checkRpgCmd = new("SELECT EXISTS (SELECT 1 FROM schema_migrations WHERE id = @id);", conn, tx);
        await using (ConfiguredAsyncDisposable checkRpgScope = checkRpgCmd.ConfigureAwait(false))
        {
            _ = checkRpgCmd.Parameters.AddWithValue("id", "0002_rpg_progression");
            object? scalar = await checkRpgCmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            hasRpgMigration = scalar is true;
        }

        if (!hasRpgMigration)
        {
            const string alterSql = """
                ALTER TABLE characters
                    ADD COLUMN IF NOT EXISTS experience      INTEGER NOT NULL DEFAULT 0,
                    ADD COLUMN IF NOT EXISTS current_mana    REAL    NOT NULL DEFAULT 80.0,
                    ADD COLUMN IF NOT EXISTS max_mana        REAL    NOT NULL DEFAULT 80.0,
                    ADD COLUMN IF NOT EXISTS current_stamina REAL    NOT NULL DEFAULT 100.0,
                    ADD COLUMN IF NOT EXISTS max_stamina     REAL    NOT NULL DEFAULT 100.0;
                """;
            NpgsqlCommand alterCmd = new(alterSql, conn, tx);
            await using (ConfiguredAsyncDisposable alterScope = alterCmd.ConfigureAwait(false))
            {
                _ = await alterCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            NpgsqlCommand markRpgCmd = new("INSERT INTO schema_migrations (id) VALUES (@id);", conn, tx);
            await using ConfiguredAsyncDisposable markRpgScope = markRpgCmd.ConfigureAwait(false);
            _ = markRpgCmd.Parameters.AddWithValue("id", "0002_rpg_progression");
            _ = await markRpgCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        bool hasPointsMigration;
        NpgsqlCommand checkPtsCmd = new("SELECT EXISTS (SELECT 1 FROM schema_migrations WHERE id = @id);", conn, tx);
        await using (ConfiguredAsyncDisposable checkPtsScope = checkPtsCmd.ConfigureAwait(false))
        {
            _ = checkPtsCmd.Parameters.AddWithValue("id", "0003_stat_and_skill_points");
            object? scalar = await checkPtsCmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            hasPointsMigration = scalar is true;
        }

        if (!hasPointsMigration)
        {
            const string alterPtsSql = """
                ALTER TABLE characters
                    ADD COLUMN IF NOT EXISTS stat_vit    INTEGER  NOT NULL DEFAULT 0,
                    ADD COLUMN IF NOT EXISTS stat_wis    INTEGER  NOT NULL DEFAULT 0,
                    ADD COLUMN IF NOT EXISTS stat_pwr    INTEGER  NOT NULL DEFAULT 0,
                    ADD COLUMN IF NOT EXISTS stat_agi    INTEGER  NOT NULL DEFAULT 0,
                    ADD COLUMN IF NOT EXISTS skill1_rank SMALLINT NOT NULL DEFAULT 0,
                    ADD COLUMN IF NOT EXISTS skill2_rank SMALLINT NOT NULL DEFAULT 0,
                    ADD COLUMN IF NOT EXISTS skill3_rank SMALLINT NOT NULL DEFAULT 0;
                """;
            NpgsqlCommand alterPtsCmd = new(alterPtsSql, conn, tx);
            await using (ConfiguredAsyncDisposable alterPtsScope = alterPtsCmd.ConfigureAwait(false))
            {
                _ = await alterPtsCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            NpgsqlCommand markPtsCmd = new("INSERT INTO schema_migrations (id) VALUES (@id);", conn, tx);
            await using ConfiguredAsyncDisposable markPtsScope = markPtsCmd.ConfigureAwait(false);
            _ = markPtsCmd.Parameters.AddWithValue("id", "0003_stat_and_skill_points");
            _ = await markPtsCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        bool hasExtMigration;
        NpgsqlCommand checkExtCmd = new("SELECT EXISTS (SELECT 1 FROM schema_migrations WHERE id = @id);", conn, tx);
        await using (ConfiguredAsyncDisposable checkExtScope = checkExtCmd.ConfigureAwait(false))
        {
            _ = checkExtCmd.Parameters.AddWithValue("id", "0004_character_class_flags_progression_blob");
            object? scalar = await checkExtCmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            hasExtMigration = scalar is true;
        }

        if (!hasExtMigration)
        {
            const string alterExtSql = """
                ALTER TABLE characters
                    ADD COLUMN IF NOT EXISTS class_id         SMALLINT NOT NULL DEFAULT 0,
                    ADD COLUMN IF NOT EXISTS character_flags  BIGINT   NOT NULL DEFAULT 1,
                    ADD COLUMN IF NOT EXISTS skill_flags      BIGINT   NOT NULL DEFAULT 0,
                    ADD COLUMN IF NOT EXISTS progression_blob BYTEA    NOT NULL DEFAULT ''::bytea;
                CREATE UNIQUE INDEX IF NOT EXISTS ux_characters_lower_name ON characters(LOWER(name));
                """;
            NpgsqlCommand alterExtCmd = new(alterExtSql, conn, tx);
            await using (ConfiguredAsyncDisposable alterExtScope = alterExtCmd.ConfigureAwait(false))
            {
                _ = await alterExtCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            bool hasLegacyCols;
            NpgsqlCommand checkLegacyCmd = new(
                "SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'characters' AND column_name = 'stat_vit');",
                conn,
                tx);
            await using (ConfiguredAsyncDisposable checkLegacyScope = checkLegacyCmd.ConfigureAwait(false))
            {
                object? scalar = await checkLegacyCmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                hasLegacyCols = scalar is true;
            }

            if (hasLegacyCols)
            {
                List<(Guid Id, long CharFlags, long SkillFlags, byte[] Blob)> updates = [];
                NpgsqlCommand selectLegacyCmd = new(
                    "SELECT id, octet_length(inventory_blob), stat_vit, stat_wis, stat_pwr, stat_agi, skill1_rank, skill2_rank, skill3_rank FROM characters WHERE octet_length(progression_blob) = 0;",
                    conn,
                    tx);
                await using (ConfiguredAsyncDisposable selectScope = selectLegacyCmd.ConfigureAwait(false))
                {
                    System.Data.Common.DbDataReader r = await selectLegacyCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                    await using ConfiguredAsyncDisposable rScope = r.ConfigureAwait(false);
                    while (await r.ReadAsync(ct).ConfigureAwait(false))
                    {
                        Guid id = r.GetGuid(0);
                        int invLen = await r.IsDBNullAsync(1, ct).ConfigureAwait(false) ? 0 : r.GetInt32(1);
                        ushort vit = (ushort)Math.Max(0, r.GetInt32(2));
                        ushort wis = (ushort)Math.Max(0, r.GetInt32(3));
                        ushort pwr = (ushort)Math.Max(0, r.GetInt32(4));
                        ushort agi = (ushort)Math.Max(0, r.GetInt32(5));
                        byte s1 = (byte)Math.Clamp((int)r.GetInt16(6), 0, 5);
                        byte s2 = (byte)Math.Clamp((int)r.GetInt16(7), 0, 5);
                        byte s3 = (byte)Math.Clamp((int)r.GetInt16(8), 0, 5);

                        byte[] blob = PackLegacyProgressionBlob(vit, wis, pwr, agi, s1, s2, s3, out long skillMask);
                        long charFlags = invLen > 0 ? 1L : 0L;
                        updates.Add((id, charFlags, skillMask, blob));
                    }
                }

                for (int i = 0; i < updates.Count; i++)
                {
                    (Guid id, long charFlags, long skillMask, byte[] blob) = updates[i];
                    NpgsqlCommand updCmd = new(
                        "UPDATE characters SET character_flags = @cf, skill_flags = @sf, progression_blob = @pb WHERE id = @id;",
                        conn,
                        tx);
                    await using ConfiguredAsyncDisposable updScope = updCmd.ConfigureAwait(false);
                    _ = updCmd.Parameters.AddWithValue("cf", charFlags);
                    _ = updCmd.Parameters.AddWithValue("sf", skillMask);
                    _ = updCmd.Parameters.AddWithValue("pb", blob);
                    _ = updCmd.Parameters.AddWithValue("id", id);
                    _ = await updCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
            }

            NpgsqlCommand markExtCmd = new("INSERT INTO schema_migrations (id) VALUES (@id);", conn, tx);
            await using ConfiguredAsyncDisposable markExtScope = markExtCmd.ConfigureAwait(false);
            _ = markExtCmd.Parameters.AddWithValue("id", "0004_character_class_flags_progression_blob");
            _ = await markExtCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        // ADR-012: server-authoritative wallet. Later additive column migrations follow the same pattern.
        await AddCharacterColumnsAsync(
            conn,
            tx,
            "0005_character_wallet",
            [
                ("gold", "BIGINT NOT NULL DEFAULT 0"),
                ("diamonds", "BIGINT NOT NULL DEFAULT 0"),
                ("wallet_sequence", "BIGINT NOT NULL DEFAULT 0"),
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

        // ADR-018: rest need. needs_rest = -1 marks "never saved" so existing characters spawn fully rested.
        await AddCharacterColumnsAsync(
            conn,
            tx,
            "0007_character_rest",
            [
                ("needs_rest", "REAL NOT NULL DEFAULT -1"),
            ],
            ct).ConfigureAwait(false);

        // ADR-026: secret key for gameplay rolls, persisted so trader stock survives restarts.
        await ApplyDdlMigrationAsync(conn, tx, "0008_gameplay_secret", GameplaySecretSql, ct).ConfigureAwait(false);

        await ApplyDdlMigrationAsync(conn, tx, "0009_trader_stock", TraderStockSql, ct).ConfigureAwait(false);

        // ADR-029: persisted status effects (key:remainingMs:stacks;...); empty for existing characters.
        await AddCharacterColumnsAsync(
            conn,
            tx,
            "0010_character_status_effects",
            [
                ("status_effects", "TEXT NOT NULL DEFAULT ''"),
            ],
            ct).ConfigureAwait(false);

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Applies an idempotent DDL migration once and records <paramref name="migrationId"/>.</summary>
    private static async Task ApplyDdlMigrationAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string migrationId,
        string ddl,
        CancellationToken ct)
    {
        NpgsqlCommand check = new("SELECT EXISTS (SELECT 1 FROM schema_migrations WHERE id = @id);", conn, tx);
        await using (ConfiguredAsyncDisposable checkScope = check.ConfigureAwait(false))
        {
            _ = check.Parameters.AddWithValue("id", migrationId);
            if (await check.ExecuteScalarAsync(ct).ConfigureAwait(false) is true)
            {
                return;
            }
        }

#pragma warning disable CA2100 // DDL is a compile-time constant, never user input.
        NpgsqlCommand apply = new(ddl, conn, tx);
#pragma warning restore CA2100
        await using (ConfiguredAsyncDisposable applyScope = apply.ConfigureAwait(false))
        {
            _ = await apply.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        NpgsqlCommand mark = new("INSERT INTO schema_migrations (id) VALUES (@id);", conn, tx);
        await using ConfiguredAsyncDisposable markScope = mark.ConfigureAwait(false);
        _ = mark.Parameters.AddWithValue("id", migrationId);
        _ = await mark.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies an additive, idempotent migration that adds missing columns to <c>characters</c> and records
    /// <paramref name="migrationId"/> in <c>schema_migrations</c>. Column names and definitions are compile-time constants.
    /// </summary>
    private static async Task AddCharacterColumnsAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string migrationId,
        (string Column, string Definition)[] columns,
        CancellationToken ct)
    {
        NpgsqlCommand check = new("SELECT EXISTS (SELECT 1 FROM schema_migrations WHERE id = @id);", conn, tx);
        await using (ConfiguredAsyncDisposable checkScope = check.ConfigureAwait(false))
        {
            _ = check.Parameters.AddWithValue("id", migrationId);
            if (await check.ExecuteScalarAsync(ct).ConfigureAwait(false) is true)
            {
                return;
            }
        }

        for (int i = 0; i < columns.Length; i++)
        {
#pragma warning disable CA2100 // Column names/definitions are compile-time constants, never user input.
            NpgsqlCommand alter = new($"ALTER TABLE characters ADD COLUMN IF NOT EXISTS {columns[i].Column} {columns[i].Definition};", conn, tx);
#pragma warning restore CA2100
            await using ConfiguredAsyncDisposable alterScope = alter.ConfigureAwait(false);
            _ = await alter.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        NpgsqlCommand mark = new("INSERT INTO schema_migrations (id) VALUES (@id);", conn, tx);
        await using ConfiguredAsyncDisposable markScope = mark.ConfigureAwait(false);
        _ = mark.Parameters.AddWithValue("id", migrationId);
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
