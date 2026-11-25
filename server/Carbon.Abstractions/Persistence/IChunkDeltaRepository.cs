// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Carbon.Abstractions.Persistence;

/// <summary>
/// Native AOT persistence store for modified chunk delta payloads.
/// </summary>
public interface IChunkDeltaRepository
{
    /// <summary>
    /// Loads every persisted chunk delta required to hydrate the authoritative world on boot.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>All saved chunk deltas.</returns>
    Task<IReadOnlyList<PersistedChunkDelta>> LoadAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Upserts modified chunk deltas in one atomic batch transaction.
    /// </summary>
    /// <param name="deltas">Serialized chunk delta snapshots.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SaveBatchAsync(IReadOnlyList<PersistedChunkDelta> deltas, CancellationToken ct = default);
}
