// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Carbon.Maps.Data;
using Carbon.Maps.Props;

namespace Carbon.Maps.Streaming;

/// <summary>
/// Player-made changes to one chunk on top of its deterministic, seed-generated baseline.
/// A chunk is regenerated from the seed whenever it is (re)loaded, and its delta is then re-applied,
/// so edits survive eviction. Deltas are also the unit persisted to storage and sent over the wire.
/// </summary>
public sealed class ChunkDelta
{
    private readonly Dictionary<int, MapTile> _tiles = new();
    private readonly Dictionary<int, PropDestructibility> _propStates = new();
    private readonly ReadOnlyDictionary<int, MapTile> _tilesView;
    private readonly ReadOnlyDictionary<int, PropDestructibility> _propStatesView;

    /// <summary>Initializes a new instance of the <see cref="ChunkDelta"/> class.</summary>
    /// <param name="chunkX">Chunk X coordinate.</param>
    /// <param name="chunkY">Chunk Y coordinate.</param>
    public ChunkDelta(int chunkX, int chunkY)
    {
        this.ChunkX = chunkX;
        this.ChunkY = chunkY;
        _tilesView = _tiles.AsReadOnly();
        _propStatesView = _propStates.AsReadOnly();
    }

    /// <summary>Gets the chunk X coordinate.</summary>
    public int ChunkX { get; }

    /// <summary>Gets the chunk Y coordinate.</summary>
    public int ChunkY { get; }

    /// <summary>Gets the packed chunk key.</summary>
    public long Key => Chunk2D.PackKey(this.ChunkX, this.ChunkY);

    /// <summary>
    /// Gets a monotonically increasing revision, bumped on every change. Clients compare it to decide
    /// whether their cached copy of the chunk is stale.
    /// </summary>
    public uint Revision { get; private set; }

    /// <summary>Gets the overridden tiles, keyed by local index (<c>localY * Chunk2D.Size + localX</c>).</summary>
    public ReadOnlyDictionary<int, MapTile> Tiles => _tilesView;

    /// <summary>Gets the overridden prop states, keyed by the local index of the tile the prop is anchored on.</summary>
    public ReadOnlyDictionary<int, PropDestructibility> PropStates => _propStatesView;

    /// <summary>Gets a value indicating whether the delta holds no changes.</summary>
    public bool IsEmpty => _tiles.Count == 0 && _propStates.Count == 0;

    /// <summary>Converts local tile coordinates into the delta's local index.</summary>
    /// <param name="localX">Local tile X.</param>
    /// <param name="localY">Local tile Y.</param>
    /// <returns>The local index.</returns>
    public static int ToLocalIndex(int localX, int localY) => (localY * Chunk2D.Size) + localX;

    /// <summary>Records a tile override.</summary>
    /// <param name="localX">Local tile X.</param>
    /// <param name="localY">Local tile Y.</param>
    /// <param name="tile">The new tile.</param>
    public void SetTile(int localX, int localY, in MapTile tile)
    {
        _tiles[ToLocalIndex(localX, localY)] = tile;
        this.Revision++;
    }

    /// <summary>Records a prop state override (damage, destruction).</summary>
    /// <param name="localX">Local tile X of the prop anchor.</param>
    /// <param name="localY">Local tile Y of the prop anchor.</param>
    /// <param name="state">The new prop state.</param>
    public void SetPropState(int localX, int localY, PropDestructibility state)
    {
        _propStates[ToLocalIndex(localX, localY)] = state;
        this.Revision++;
    }

    /// <summary>Overrides the revision, used when importing a persisted or received delta.</summary>
    /// <param name="revision">The stored revision.</param>
    public void RestoreRevision(uint revision) => this.Revision = revision;

    /// <summary>Applies this delta onto a freshly generated chunk.</summary>
    /// <param name="chunk">The chunk, already populated from the seed.</param>
    public void ApplyTo(Chunk2D chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        foreach (KeyValuePair<int, MapTile> kvp in _tiles)
        {
            chunk.SetTile(kvp.Key & Chunk2D.SizeMask, kvp.Key >> Chunk2D.SizeBits, kvp.Value);
        }

        foreach (KeyValuePair<int, PropDestructibility> kvp in _propStates)
        {
            int index = chunk.FindPropIndexAt(kvp.Key & Chunk2D.SizeMask, kvp.Key >> Chunk2D.SizeBits);
            if (index >= 0)
            {
                chunk.SetProp(index, chunk.Props[index] with { Destructibility = kvp.Value });
            }
        }
    }
}
