// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.Persistence;
using Microsoft.Data.Sqlite;

namespace Carbon.Infrastructure.Persistence.Sqlite;

/// <summary>SQLite store of trader sales per restock epoch (<c>trader_stock</c>, ADR-025).</summary>
public sealed class SqliteTraderStockRepository : ITraderStockRepository
{
    private const string LoadAllSql = "SELECT trader_key, epoch, sold FROM trader_stock;";

    private const string UpsertSql = """
        INSERT INTO trader_stock (trader_key, epoch, sold, updated_at_utc)
        VALUES (@key, @epoch, @sold, @updated)
        ON CONFLICT (trader_key) DO UPDATE
        SET epoch = excluded.epoch, sold = excluded.sold, updated_at_utc = excluded.updated_at_utc;
        """;

    private readonly SqliteConnectionFactory _factory;

    /// <summary>Initializes a new instance of the <see cref="SqliteTraderStockRepository"/> class.</summary>
    /// <param name="factory">SQLite connection factory.</param>
    public SqliteTraderStockRepository(SqliteConnectionFactory factory) =>
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PersistedTraderSales>> LoadAllAsync(CancellationToken ct = default)
    {
        SqliteConnection conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable connScope = conn.ConfigureAwait(false);
        SqliteCommand cmd = new(LoadAllSql, conn);
        await using ConfiguredAsyncDisposable cmdScope = cmd.ConfigureAwait(false);
        List<PersistedTraderSales> results = [];
        SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable readerScope = reader.ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            byte[] sold = await reader.GetFieldValueAsync<byte[]>(2, ct).ConfigureAwait(false);
            results.Add(new PersistedTraderSales(reader.GetInt64(0), reader.GetInt64(1), sold));
        }

        return results;
    }

    /// <inheritdoc/>
    public async Task SaveBatchAsync(IReadOnlyList<PersistedTraderSales> sales, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sales);
        if (sales.Count == 0)
        {
            return;
        }

        SqliteConnection conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable connScope = conn.ConfigureAwait(false);
        SqliteTransaction tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable txScope = tx.ConfigureAwait(false);
        SqliteCommand cmd = new(UpsertSql, conn, tx);
        await using ConfiguredAsyncDisposable cmdScope = cmd.ConfigureAwait(false);
        SqliteParameter pKey = cmd.Parameters.Add("@key", SqliteType.Integer);
        SqliteParameter pEpoch = cmd.Parameters.Add("@epoch", SqliteType.Integer);
        SqliteParameter pSold = cmd.Parameters.Add("@sold", SqliteType.Blob);
        SqliteParameter pUpdated = cmd.Parameters.Add("@updated", SqliteType.Integer);
        await cmd.PrepareAsync(ct).ConfigureAwait(false);
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (int i = 0; i < sales.Count; i++)
        {
            pKey.Value = sales[i].TraderKey;
            pEpoch.Value = sales[i].Epoch;
            pSold.Value = sales[i].Sold;
            pUpdated.Value = now;
            _ = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }
}
