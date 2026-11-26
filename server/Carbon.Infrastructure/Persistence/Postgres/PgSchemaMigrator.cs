// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
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
            max_stamina          REAL        NOT NULL DEFAULT 100.0
        );

        CREATE INDEX IF NOT EXISTS ix_characters_account_id ON characters(account_id);

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

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }
}
