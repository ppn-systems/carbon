// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using Carbon.Maps.Data;

namespace Carbon.Desktop.Networking.Session;

/// <summary>How a chunk delta request ended.</summary>
internal enum ChunkRequestResult
{
    /// <summary>The server sent the chunk's delta.</summary>
    Delta,

    /// <summary>The stream ended with no delta: an unmodified chunk, or a request the server refused.</summary>
    NoDelta,

    /// <summary>The request failed (timeout, disconnect).</summary>
    Failed,
}

/// <summary>
/// Which chunk deltas the client has asked the server for, around the local player (the ring of <see cref="Radius"/>
/// chunks). Called from the frame (<see cref="Collect"/>) and from request completions (<see cref="Completed"/>).
/// Requests go out when the player's chunk changes, and a request that brought no delta is forgotten, so the next chunk
/// change asks again (audit SA-10): the server refuses a chunk beyond its reach of the player, and a client whose
/// predicted position ran two chunks ahead (after a stall) would otherwise never load that chunk's buildings or edits.
/// </summary>
internal sealed class ChunkDeltaRequests
{
    /// <summary>Chunks requested around the player's chunk, on each axis (the server's stream radius).</summary>
    public const int Radius = 2;

    /// <summary>The most chunks one <see cref="Collect"/> can return.</summary>
    public const int MaxPerCollect = ((2 * Radius) + 1) * ((2 * Radius) + 1);

    private readonly Lock _gate = new();
    private readonly HashSet<long> _requested = [];
    private long _center = long.MinValue;

    /// <summary>
    /// When the player's chunk changed since the last call, writes the packed keys of the chunks around
    /// (<paramref name="centerChunkX"/>, <paramref name="centerChunkY"/>) that are not requested yet into
    /// <paramref name="output"/>, and marks them requested. Writes nothing while the player stays in the same chunk.
    /// </summary>
    /// <param name="centerChunkX">The player's chunk X.</param>
    /// <param name="centerChunkY">The player's chunk Y.</param>
    /// <param name="output">At least <see cref="MaxPerCollect"/> keys.</param>
    /// <returns>How many keys were written.</returns>
    public int Collect(int centerChunkX, int centerChunkY, Span<long> output)
    {
        int count = 0;
        long center = Chunk2D.PackKey(centerChunkX, centerChunkY);
        lock (_gate)
        {
            if (center == _center)
            {
                return 0;
            }

            _center = center;
            for (int dy = -Radius; dy <= Radius; dy++)
            {
                for (int dx = -Radius; dx <= Radius; dx++)
                {
                    long key = Chunk2D.PackKey(centerChunkX + dx, centerChunkY + dy);
                    if (_requested.Add(key))
                    {
                        output[count++] = key;
                    }
                }
            }
        }

        return count;
    }

    /// <summary>
    /// A request ended. One that failed or brought no delta (an unmodified chunk, or one the server refused) is forgotten,
    /// so it is asked again at the next chunk change. A chunk whose delta arrived is never asked again; its later changes
    /// arrive live.
    /// </summary>
    /// <param name="key">The chunk's packed key.</param>
    /// <param name="result">How it ended.</param>
    public void Completed(long key, ChunkRequestResult result)
    {
        if (result == ChunkRequestResult.Delta)
        {
            return;
        }

        lock (_gate)
        {
            _ = _requested.Remove(key);
        }
    }

    /// <summary>Forgets every request (a new world or a reconnect).</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _requested.Clear();
            _center = long.MinValue;
        }
    }
}
