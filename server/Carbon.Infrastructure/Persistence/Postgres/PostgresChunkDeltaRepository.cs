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
/// Pure <c>Npgsql</c> repository persisting serialized <c>ChunkDelta</c> blobs in atomic batches.
/// </summary>
public sealed class PostgresChunkDeltaRepository : IChunkDeltaRepository
{
    private const string LoadAllSql = """
        SELECT chunk_x, chunk_y, revision, generator_version, payload, updated_at_utc
        FROM world_chunk_deltas;
        """;

    private const string UpsertSql = """
        INSERT INTO world_chunk_deltas (chunk_x, chunk_y, revision, generator_version, payload, updated_at_utc)
        VALUES (@cx, @cy, @rev, @ver, @payload, @updated)
        ON CONFLICT (chunk_x, chunk_y) DO UPDATE
        SET revision = EXCLUDED.revision,
            generator_version = EXCLUDED.generator_version,
            payload = EXCLUDED.payload,
            updated_at_utc = EXCLUDED.updated_at_utc;
        """;

    private readonly NpgsqlDataSource _dataSource;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresChunkDeltaRepository"/> class.
    /// </summary>
    /// <param name="dataSource">PostgreSQL data source.</param>
    public PostgresChunkDeltaRepository(NpgsqlDataSource dataSource) =>
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PersistedChunkDelta>> LoadAllAsync(CancellationToken ct = default)
    {
        NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable connScope = conn.ConfigureAwait(false);

        NpgsqlCommand cmd = new(LoadAllSql, conn);
        await using ConfiguredAsyncDisposable cmdScope = cmd.ConfigureAwait(false);

        List<PersistedChunkDelta> results = [];
        DbDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable readerScope = reader.ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            byte[] payload = await reader.GetFieldValueAsync<byte[]>(4, ct).ConfigureAwait(false);
            DateTimeOffset updated = await reader.GetFieldValueAsync<DateTimeOffset>(5, ct).ConfigureAwait(false);
            results.Add(new PersistedChunkDelta(
                reader.GetInt32(0),
                reader.GetInt32(1),
                (uint)reader.GetInt64(2),
                (ushort)reader.GetInt32(3),
                payload,
                updated));
        }

        return results;
    }

    /// <inheritdoc/>
    public async Task SaveBatchAsync(IReadOnlyList<PersistedChunkDelta> deltas, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(deltas);
        if (deltas.Count == 0)
        {
            return;
        }

        NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable connScope = conn.ConfigureAwait(false);

        NpgsqlTransaction tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable txScope = tx.ConfigureAwait(false);

        NpgsqlBatch batch = new(conn, tx);
        await using ConfiguredAsyncDisposable batchScope = batch.ConfigureAwait(false);

        for (int i = 0; i < deltas.Count; i++)
        {
            PersistedChunkDelta d = deltas[i];
            NpgsqlBatchCommand item = new(UpsertSql);
            _ = item.Parameters.Add(new NpgsqlParameter("cx", NpgsqlDbType.Integer) { Value = d.ChunkX });
            _ = item.Parameters.Add(new NpgsqlParameter("cy", NpgsqlDbType.Integer) { Value = d.ChunkY });
            _ = item.Parameters.Add(new NpgsqlParameter("rev", NpgsqlDbType.Bigint) { Value = (long)d.Revision });
            _ = item.Parameters.Add(new NpgsqlParameter("ver", NpgsqlDbType.Integer) { Value = (int)d.GeneratorVersion });
            _ = item.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Bytea) { Value = d.Payload });
            _ = item.Parameters.Add(new NpgsqlParameter("updated", NpgsqlDbType.TimestampTz) { Value = d.UpdatedAtUtc });
            batch.BatchCommands.Add(item);
        }

        _ = await batch.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }
}
