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
/// </summary>
internal sealed class ChunkDeltaRequests
{
    /// <summary>Chunks requested around the player's chunk, on each axis (the server's stream radius).</summary>
    public const int Radius = 2;

    /// <summary>The most chunks one <see cref="Collect"/> can return.</summary>
    public const int MaxPerCollect = ((2 * Radius) + 1) * ((2 * Radius) + 1);

    private readonly Lock _gate = new();
    private readonly HashSet<long> _requested = [];

    /// <summary>
    /// Writes the packed keys of the chunks around (<paramref name="centerChunkX"/>, <paramref name="centerChunkY"/>) that
    /// have not been requested yet into <paramref name="output"/>, and marks them requested.
    /// </summary>
    /// <param name="centerChunkX">The player's chunk X.</param>
    /// <param name="centerChunkY">The player's chunk Y.</param>
    /// <param name="output">At least <see cref="MaxPerCollect"/> keys.</param>
    /// <returns>How many keys were written.</returns>
    public int Collect(int centerChunkX, int centerChunkY, Span<long> output)
    {
        int count = 0;
        lock (_gate)
        {
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

    /// <summary>A request ended. A failed one is forgotten, so it is asked again.</summary>
    /// <param name="key">The chunk's packed key.</param>
    /// <param name="result">How it ended.</param>
    public void Completed(long key, ChunkRequestResult result)
    {
        if (result != ChunkRequestResult.Failed)
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
        }
    }
}
