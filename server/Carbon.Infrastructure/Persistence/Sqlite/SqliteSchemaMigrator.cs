// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
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
            stat_vit             INTEGER NOT NULL DEFAULT 0,
            stat_wis             INTEGER NOT NULL DEFAULT 0,
            stat_pwr             INTEGER NOT NULL DEFAULT 0,
            stat_agi             INTEGER NOT NULL DEFAULT 0,
            skill1_rank          INTEGER NOT NULL DEFAULT 0,
            skill2_rank          INTEGER NOT NULL DEFAULT 0,
            skill3_rank          INTEGER NOT NULL DEFAULT 0
        );

        CREATE INDEX IF NOT EXISTS ix_characters_account_id ON characters(account_id);

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

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }
}
