// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.Persistence;
using Microsoft.Data.Sqlite;

namespace Carbon.Infrastructure.Persistence.Sqlite;

/// <summary>
/// Pure SQLite repository for serialized <c>ChunkDelta</c> records using prepared transactional batches.
/// </summary>
public sealed class SqliteChunkDeltaRepository : IChunkDeltaRepository
{
    private const string LoadAllSql = """
        SELECT chunk_x, chunk_y, revision, generator_version, payload, updated_at_utc
        FROM world_chunk_deltas;
        """;

    private const string UpsertSql = """
        INSERT INTO world_chunk_deltas (chunk_x, chunk_y, revision, generator_version, payload, updated_at_utc)
        VALUES (@cx, @cy, @rev, @ver, @payload, @updated)
        ON CONFLICT (chunk_x, chunk_y) DO UPDATE
        SET revision = excluded.revision,
            generator_version = excluded.generator_version,
            payload = excluded.payload,
            updated_at_utc = excluded.updated_at_utc;
        """;

    private readonly SqliteConnectionFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteChunkDeltaRepository"/> class.
    /// </summary>
    /// <param name="factory">SQLite connection factory.</param>
    public SqliteChunkDeltaRepository(SqliteConnectionFactory factory) =>
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PersistedChunkDelta>> LoadAllAsync(CancellationToken ct = default)
    {
        SqliteConnection conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable connScope = conn.ConfigureAwait(false);

        SqliteCommand cmd = new(LoadAllSql, conn);
        await using ConfiguredAsyncDisposable cmdScope = cmd.ConfigureAwait(false);

        List<PersistedChunkDelta> results = [];
        SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable readerScope = reader.ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            byte[] payload = await reader.GetFieldValueAsync<byte[]>(4, ct).ConfigureAwait(false);
            results.Add(new PersistedChunkDelta(
                reader.GetInt32(0),
                reader.GetInt32(1),
                (uint)reader.GetInt64(2),
                (ushort)reader.GetInt32(3),
                payload,
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(5))));
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

        SqliteConnection conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable connScope = conn.ConfigureAwait(false);

        SqliteTransaction tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable txScope = tx.ConfigureAwait(false);

        SqliteCommand cmd = new(UpsertSql, conn, tx);
        await using ConfiguredAsyncDisposable cmdScope = cmd.ConfigureAwait(false);

        SqliteParameter pCx = cmd.Parameters.Add("@cx", SqliteType.Integer);
        SqliteParameter pCy = cmd.Parameters.Add("@cy", SqliteType.Integer);
        SqliteParameter pRev = cmd.Parameters.Add("@rev", SqliteType.Integer);
        SqliteParameter pVer = cmd.Parameters.Add("@ver", SqliteType.Integer);
        SqliteParameter pPayload = cmd.Parameters.Add("@payload", SqliteType.Blob);
        SqliteParameter pUpdated = cmd.Parameters.Add("@updated", SqliteType.Integer);
        await cmd.PrepareAsync(ct).ConfigureAwait(false);

        for (int i = 0; i < deltas.Count; i++)
        {
            PersistedChunkDelta d = deltas[i];
            pCx.Value = d.ChunkX;
            pCy.Value = d.ChunkY;
            pRev.Value = (long)d.Revision;
            pVer.Value = (int)d.GeneratorVersion;
            pPayload.Value = d.Payload;
            pUpdated.Value = d.UpdatedAtUtc.ToUnixTimeMilliseconds();
            _ = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }
}
