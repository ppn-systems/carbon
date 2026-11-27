// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Carbon.Abstractions.Persistence;
using Carbon.Infrastructure.Persistence.Sqlite;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>
/// A database created by an older build must be brought up to the current schema by <see cref="SqliteSchemaMigrator"/>.
/// Tables added to <c>0001_initial</c> after it shipped never reach such databases, so every table needs its own migration.
/// </summary>
public sealed class SqliteSchemaUpgradeTests
{
    [Fact]
    public async Task MigrateAsync_DatabaseFromBeforeVillages_CreatesTraderStockTable()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-upgrade-test-{Guid.NewGuid():N}.db");
        try
        {
            using SqliteConnectionFactory factory = new($"Data Source={dbPath};Pooling=False");
            SqliteSchemaMigrator migrator = new(factory);
            await migrator.MigrateAsync().ConfigureAwait(true);

            // Reproduce a database created before ADR-025: every earlier migration is recorded, but trader_stock is absent.
            await ExecuteAsync(factory, "DROP TABLE trader_stock; DELETE FROM schema_migrations WHERE id = '0009_trader_stock';").ConfigureAwait(true);

            await migrator.MigrateAsync().ConfigureAwait(true);

            SqliteTraderStockRepository traders = new(factory);
            await traders.SaveBatchAsync([new PersistedTraderSales(TraderKey: 7, Epoch: 3, Sold: [1, 2])]).ConfigureAwait(true);
            IReadOnlyList<PersistedTraderSales> loaded = await traders.LoadAllAsync().ConfigureAwait(true);
            loaded.Should().ContainSingle(s => s.TraderKey == 7 && s.Epoch == 3 && s.Sold.Length == 2);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(dbPath);
        }
    }

    private static async Task ExecuteAsync(SqliteConnectionFactory factory, string sql)
    {
        SqliteConnection conn = await factory.OpenAsync().ConfigureAwait(true);
        await using (conn.ConfigureAwait(true))
        {
#pragma warning disable CA2100 // Test-only constant SQL.
            SqliteCommand cmd = new(sql, conn);
#pragma warning restore CA2100
            await using (cmd.ConfigureAwait(true))
            {
                _ = await cmd.ExecuteNonQueryAsync().ConfigureAwait(true);
            }
        }
    }
}
