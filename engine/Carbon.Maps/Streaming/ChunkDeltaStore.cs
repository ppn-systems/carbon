// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Concurrent;
using System.Collections.Generic;
using Carbon.Maps.Data;

namespace Carbon.Maps.Streaming;

/// <summary>
/// Player-made changes per chunk (edited tiles, damaged props), kept for every edited chunk whether it is
/// loaded or not, so edits survive eviction and can be saved or sent to clients. Reads are lock-free;
/// writers synchronise through the owning world's lock.
/// </summary>
public sealed class ChunkDeltaStore
{
    private readonly ConcurrentDictionary<long, ChunkDelta> _deltas = new();

    /// <summary>Gets every recorded delta.</summary>
    public ICollection<ChunkDelta> All => _deltas.Values;

    /// <summary>Gets the number of edited chunks.</summary>
    public int Count => _deltas.Count;

    /// <summary>Gets the delta of a chunk.</summary>
    /// <param name="chunkX">Chunk X.</param>
    /// <param name="chunkY">Chunk Y.</param>
    /// <param name="delta">The delta, when the chunk has been edited.</param>
    /// <returns><c>true</c> when the chunk has a delta.</returns>
    public bool TryGet(int chunkX, int chunkY, out ChunkDelta? delta) =>
        _deltas.TryGetValue(Chunk2D.PackKey(chunkX, chunkY), out delta);

    /// <summary>Gets the edited tile at a local position of a chunk, if any.</summary>
    /// <param name="chunkX">Chunk X.</param>
    /// <param name="chunkY">Chunk Y.</param>
    /// <param name="localX">Local X.</param>
    /// <param name="localY">Local Y.</param>
    /// <param name="tile">The edited tile.</param>
    /// <returns><c>true</c> when the tile was edited.</returns>
    public bool TryGetTile(int chunkX, int chunkY, int localX, int localY, out MapTile tile)
    {
        if (this.TryGet(chunkX, chunkY, out ChunkDelta? delta) && delta != null &&
            delta.Tiles.TryGetValue(ChunkDelta.ToLocalIndex(localX, localY), out tile))
        {
            return true;
        }

        tile = default;
        return false;
    }

    /// <summary>Gets the delta of a chunk, creating an empty one. Call under the world lock.</summary>
    /// <param name="chunkX">Chunk X.</param>
    /// <param name="chunkY">Chunk Y.</param>
    /// <returns>The delta.</returns>
    internal ChunkDelta GetOrAdd(int chunkX, int chunkY) =>
        _deltas.GetOrAdd(Chunk2D.PackKey(chunkX, chunkY), _ => new ChunkDelta(chunkX, chunkY));

    /// <summary>Stores a delta, replacing any existing one. Call under the world lock.</summary>
    /// <param name="delta">The delta.</param>
    internal void Set(ChunkDelta delta) => _deltas[delta.Key] = delta;
}
