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

/// <summary>PostgreSQL store of trader sales per restock epoch (<c>trader_stock</c>, ADR-025).</summary>
public sealed class PgTraderStockRepository : ITraderStockRepository
{
    private const string LoadAllSql = "SELECT trader_key, epoch, sold FROM trader_stock;";

    private const string UpsertSql = """
        INSERT INTO trader_stock (trader_key, epoch, sold, updated_at_utc)
        VALUES (@key, @epoch, @sold, @updated)
        ON CONFLICT (trader_key) DO UPDATE
        SET epoch = EXCLUDED.epoch, sold = EXCLUDED.sold, updated_at_utc = EXCLUDED.updated_at_utc;
        """;

    private readonly NpgsqlDataSource _dataSource;

    /// <summary>Initializes a new instance of the <see cref="PgTraderStockRepository"/> class.</summary>
    /// <param name="dataSource">Npgsql data source.</param>
    public PgTraderStockRepository(NpgsqlDataSource dataSource) =>
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PersistedTraderSales>> LoadAllAsync(CancellationToken ct = default)
    {
        NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable connScope = conn.ConfigureAwait(false);
        NpgsqlCommand cmd = new(LoadAllSql, conn);
        await using ConfiguredAsyncDisposable cmdScope = cmd.ConfigureAwait(false);
        List<PersistedTraderSales> results = [];
        DbDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
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

        NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable connScope = conn.ConfigureAwait(false);
        NpgsqlTransaction tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable txScope = tx.ConfigureAwait(false);
        NpgsqlBatch batch = new(conn, tx);
        await using ConfiguredAsyncDisposable batchScope = batch.ConfigureAwait(false);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        for (int i = 0; i < sales.Count; i++)
        {
            NpgsqlBatchCommand item = new(UpsertSql);
            _ = item.Parameters.Add(new NpgsqlParameter("key", NpgsqlDbType.Bigint) { Value = sales[i].TraderKey });
            _ = item.Parameters.Add(new NpgsqlParameter("epoch", NpgsqlDbType.Bigint) { Value = sales[i].Epoch });
            _ = item.Parameters.Add(new NpgsqlParameter("sold", NpgsqlDbType.Bytea) { Value = sales[i].Sold });
            _ = item.Parameters.Add(new NpgsqlParameter("updated", NpgsqlDbType.TimestampTz) { Value = now });
            batch.BatchCommands.Add(item);
        }

        _ = await batch.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }
}
