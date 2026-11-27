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
    private Chunk2D[] _activeSnapshot = [];

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

    /// <summary>Gets a zero-allocation read-only span over the currently resident chunks.</summary>
    public ReadOnlySpan<Chunk2D> Active => Volatile.Read(ref _activeSnapshot);

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
            this.RebuildActiveSnapshotLocked();
            return chunk;
        }
    }

    /// <summary>Regenerates a resident chunk atomically so readers never see a partially reset chunk. Call under the lock.</summary>
    public void RebuildIfResident(int chunkX, int chunkY)
    {
        long key = Chunk2D.PackKey(chunkX, chunkY);
        if (!_active.ContainsKey(key))
        {
            return;
        }

        Chunk2D replacement = _pool.TryDequeue(out Chunk2D? recycled) ? recycled : new Chunk2D(chunkX, chunkY);
        this.Build(replacement, chunkX, chunkY);
        _active[key] = replacement;
        this.RebuildActiveSnapshotLocked();
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
            // The snapshot holds exactly the resident chunks: every change to _active happens under this lock and
            // rebuilds it. Walking the array instead of the ConcurrentDictionary avoids allocating an enumerator
            // on every call, which runs for each player streaming update (audit round 2, PERF-4).
            _evictionBuffer.Clear();
            Chunk2D[] resident = _activeSnapshot;
            for (int i = 0; i < resident.Length; i++)
            {
                Chunk2D chunk = resident[i];
                if (Math.Max(Math.Abs(chunk.ChunkX - centerChunkX), Math.Abs(chunk.ChunkY - centerChunkY)) > evictRadius)
                {
                    _evictionBuffer.Add(chunk.Key);
                }
            }

            bool evictedAny = false;
            for (int i = 0; i < _evictionBuffer.Count; i++)
            {
                if (_active.TryRemove(_evictionBuffer[i], out Chunk2D? evicted))
                {
                    _pool.Enqueue(evicted);
                    evictedAny = true;
                }
            }

            if (evictedAny)
            {
                this.RebuildActiveSnapshotLocked();
            }
        }
    }

    private void RebuildActiveSnapshotLocked()
    {
        int count = _active.Count;
        if (count == 0)
        {
            Volatile.Write(ref _activeSnapshot, []);
            return;
        }

        Chunk2D[] next = new Chunk2D[count];
        int index = 0;
        foreach (KeyValuePair<long, Chunk2D> kvp in _active)
        {
            next[index++] = kvp.Value;
        }

        Array.Sort(next, static (a, b) => a.ChunkY != b.ChunkY ? a.ChunkY.CompareTo(b.ChunkY) : a.ChunkX.CompareTo(b.ChunkX));
        Volatile.Write(ref _activeSnapshot, next);
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
