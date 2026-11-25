// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;

namespace Carbon.Maps.Streaming;

/// <summary>
/// Chunk residency: generates chunks on demand (re-applying recorded deltas), recycles far chunks through
/// an object pool and keeps lookups of resident chunks lock-free.
/// </summary>
internal sealed class ChunkStreamer
{
    private readonly ProceduralWorldGenerator _generator;
    private readonly ChunkDeltaStore _deltas;
    private readonly Lock _gate;
    private readonly float _frequency;
    private readonly float _tileSize;
    private readonly ConcurrentDictionary<long, Chunk2D> _active = new();
    private readonly ConcurrentQueue<Chunk2D> _pool = new();
    private readonly List<long> _evictionBuffer = new();

    /// <summary>Initializes a new instance of the <see cref="ChunkStreamer"/> class.</summary>
    /// <param name="generator">The world generator.</param>
    /// <param name="deltas">Recorded edits, applied to every chunk built.</param>
    /// <param name="gate">The world lock serialising creation, eviction and edits.</param>
    /// <param name="frequency">Macro noise frequency.</param>
    /// <param name="tileSize">World units per tile.</param>
    public ChunkStreamer(ProceduralWorldGenerator generator, ChunkDeltaStore deltas, Lock gate, float frequency, float tileSize)
    {
        _generator = generator;
        _deltas = deltas;
        _gate = gate;
        _frequency = frequency;
        _tileSize = tileSize;
    }

    /// <summary>Gets the resident chunks.</summary>
    public ICollection<Chunk2D> Active => _active.Values;

    /// <summary>Gets the number of resident chunks.</summary>
    public int ActiveCount => _active.Count;

    /// <summary>Gets the number of pooled chunks.</summary>
    public int PooledCount => _pool.Count;

    /// <summary>Gets a resident chunk without generating it.</summary>
    public bool TryGet(int chunkX, int chunkY, out Chunk2D? chunk) =>
        _active.TryGetValue(Chunk2D.PackKey(chunkX, chunkY), out chunk);

    /// <summary>Gets a resident chunk or generates it (recycling a pooled instance).</summary>
    public Chunk2D GetOrCreate(int chunkX, int chunkY)
    {
        long key = Chunk2D.PackKey(chunkX, chunkY);
        if (_active.TryGetValue(key, out Chunk2D? existing))
        {
            return existing;
        }

        lock (_gate)
        {
            // Double-check: another thread may have created it while we waited.
            if (_active.TryGetValue(key, out existing))
            {
                return existing;
            }

            Chunk2D chunk = _pool.TryDequeue(out Chunk2D? recycled) ? recycled : new Chunk2D(chunkX, chunkY);
            this.Build(chunk, chunkX, chunkY);

            // Publish only once fully populated, so readers never see a half-built chunk.
            _active[key] = chunk;
            return chunk;
        }
    }

    /// <summary>Regenerates a resident chunk so a newly imported delta takes effect. Call under the lock.</summary>
    public void RebuildIfResident(int chunkX, int chunkY)
    {
        if (this.TryGet(chunkX, chunkY, out Chunk2D? chunk) && chunk != null)
        {
            this.Build(chunk, chunkX, chunkY);
        }
    }

    /// <summary>Loads every chunk within <paramref name="loadRadius"/> and recycles those beyond <paramref name="evictRadius"/>.</summary>
    public void Stream(int centerChunkX, int centerChunkY, int loadRadius, int evictRadius)
    {
        for (int cy = centerChunkY - loadRadius; cy <= centerChunkY + loadRadius; cy++)
        {
            for (int cx = centerChunkX - loadRadius; cx <= centerChunkX + loadRadius; cx++)
            {
                _ = this.GetOrCreate(cx, cy);
            }
        }

        // Evicted chunks keep their edits in the delta store.
        lock (_gate)
        {
            _evictionBuffer.Clear();
            foreach (KeyValuePair<long, Chunk2D> kvp in _active)
            {
                (int kx, int ky) = Chunk2D.UnpackKey(kvp.Key);
                if (Math.Max(Math.Abs(kx - centerChunkX), Math.Abs(ky - centerChunkY)) > evictRadius)
                {
                    _evictionBuffer.Add(kvp.Key);
                }
            }

            for (int i = 0; i < _evictionBuffer.Count; i++)
            {
                if (_active.TryRemove(_evictionBuffer[i], out Chunk2D? evicted))
                {
                    _pool.Enqueue(evicted);
                }
            }
        }
    }

    private void Build(Chunk2D chunk, int chunkX, int chunkY)
    {
        _generator.PopulateChunk(chunk, chunkX, chunkY, _frequency, _tileSize);
        if (_deltas.TryGet(chunkX, chunkY, out ChunkDelta? delta) && delta != null)
        {
            delta.ApplyTo(chunk); // player-made changes on top of the deterministic baseline
        }
    }
}
