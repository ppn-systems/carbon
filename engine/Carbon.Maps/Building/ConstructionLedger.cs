// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Maps.Data;

namespace Carbon.Maps.Building;

/// <summary>
/// In-memory store of construction sites (blueprints / frames) and per-tile ownership records of completed structures.
/// When owned by a <see cref="Streaming.ChunkWorld2D"/> every change is mirrored into the chunk delta so it is
/// persisted and synced like <see cref="BuildingGrid"/> structures (ADR-001). Not thread-safe: mutate under the world lock.
/// </summary>
public sealed class ConstructionLedger
{
    private readonly Dictionary<(int X, int Y), ConstructionSite> _sites = new();
    private readonly Dictionary<(int X, int Y), (int X, int Y)> _reservations = new();
    private readonly Dictionary<(int X, int Y), StructureRecord> _records = new();
    private readonly Action<int, int, ConstructionSite?>? _onSiteChanged;
    private readonly Action<int, int, StructureRecord?>? _onRecordChanged;

    /// <summary>Initializes a new standalone instance of the <see cref="ConstructionLedger"/> class.</summary>
    public ConstructionLedger()
    {
    }

    internal ConstructionLedger(Action<int, int, ConstructionSite?> onSiteChanged, Action<int, int, StructureRecord?> onRecordChanged)
    {
        _onSiteChanged = onSiteChanged;
        _onRecordChanged = onRecordChanged;
    }

    /// <summary>Gets the number of active construction sites.</summary>
    public int SiteCount => _sites.Count;

    /// <summary>Gets the number of tiles carrying a structure record.</summary>
    public int RecordCount => _records.Count;

    /// <summary>Resolves the site covering a world tile.</summary>
    /// <param name="worldTileX">World tile X (any footprint tile).</param>
    /// <param name="worldTileY">World tile Y (any footprint tile).</param>
    /// <param name="anchorX">Anchor tile X of the site.</param>
    /// <param name="anchorY">Anchor tile Y of the site.</param>
    /// <param name="site">The site.</param>
    /// <returns><c>true</c> when a site covers the tile.</returns>
    public bool TryGetSiteAt(int worldTileX, int worldTileY, out int anchorX, out int anchorY, out ConstructionSite site)
    {
        if (_reservations.TryGetValue((worldTileX, worldTileY), out (int X, int Y) anchor) &&
            _sites.TryGetValue(anchor, out ConstructionSite? found))
        {
            (anchorX, anchorY) = anchor;
            site = found;
            return true;
        }

        anchorX = 0;
        anchorY = 0;
        site = null!;
        return false;
    }

    /// <summary>Evaluates whether a site reserves the tile.</summary>
    /// <param name="worldTileX">World tile X.</param>
    /// <param name="worldTileY">World tile Y.</param>
    /// <returns><c>true</c> when reserved.</returns>
    public bool IsReserved(int worldTileX, int worldTileY) => _reservations.ContainsKey((worldTileX, worldTileY));

    /// <summary>Gets the ownership record of a completed structure tile.</summary>
    /// <param name="worldTileX">World tile X.</param>
    /// <param name="worldTileY">World tile Y.</param>
    /// <param name="record">The record.</param>
    /// <returns><c>true</c> when the tile has a record.</returns>
    public bool TryGetRecord(int worldTileX, int worldTileY, out StructureRecord record) =>
        _records.TryGetValue((worldTileX, worldTileY), out record);

    internal void AddSite(int anchorX, int anchorY, ConstructionSite site)
    {
        _sites[(anchorX, anchorY)] = site;
        for (int dy = 0; dy < site.Height; dy++)
        {
            for (int dx = 0; dx < site.Width; dx++)
            {
                _reservations[(anchorX + dx, anchorY + dy)] = (anchorX, anchorY);
            }
        }

        _onSiteChanged?.Invoke(anchorX, anchorY, site);
    }

    internal void NotifySiteChanged(int anchorX, int anchorY, ConstructionSite site) =>
        _onSiteChanged?.Invoke(anchorX, anchorY, site);

    internal bool RemoveSite(int anchorX, int anchorY)
    {
        if (!this.RemoveSiteSilently(anchorX, anchorY))
        {
            return false;
        }

        _onSiteChanged?.Invoke(anchorX, anchorY, null);
        return true;
    }

    internal void SetRecord(int worldTileX, int worldTileY, StructureRecord record)
    {
        _records[(worldTileX, worldTileY)] = record;
        _onRecordChanged?.Invoke(worldTileX, worldTileY, record);
    }

    internal void RemoveRecord(int worldTileX, int worldTileY)
    {
        if (_records.Remove((worldTileX, worldTileY)))
        {
            _onRecordChanged?.Invoke(worldTileX, worldTileY, null);
        }
    }

    /// <summary>
    /// Replaces the sites anchored in, and records located in, a chunk with imported delta state without firing callbacks.
    /// </summary>
    internal void ApplyChunk(
        int chunkX,
        int chunkY,
        IReadOnlyDictionary<int, ConstructionSite> sites,
        IReadOnlyDictionary<int, StructureRecord> records)
    {
        int baseX = chunkX * Chunk2D.Size;
        int baseY = chunkY * Chunk2D.Size;
        for (int ly = 0; ly < Chunk2D.Size; ly++)
        {
            for (int lx = 0; lx < Chunk2D.Size; lx++)
            {
                int wx = baseX + lx;
                int wy = baseY + ly;
                int localIndex = (ly * Chunk2D.Size) + lx;

                _ = this.RemoveSiteSilently(wx, wy);
                if (sites.TryGetValue(localIndex, out ConstructionSite? site))
                {
                    ConstructionSite copy = site.Clone();
                    _sites[(wx, wy)] = copy;
                    for (int dy = 0; dy < copy.Height; dy++)
                    {
                        for (int dx = 0; dx < copy.Width; dx++)
                        {
                            _reservations[(wx + dx, wy + dy)] = (wx, wy);
                        }
                    }
                }

                if (records.TryGetValue(localIndex, out StructureRecord record))
                {
                    _records[(wx, wy)] = record;
                }
                else
                {
                    _ = _records.Remove((wx, wy));
                }
            }
        }
    }

    private bool RemoveSiteSilently(int anchorX, int anchorY)
    {
        if (!_sites.Remove((anchorX, anchorY), out ConstructionSite? site))
        {
            return false;
        }

        for (int dy = 0; dy < site.Height; dy++)
        {
            for (int dx = 0; dx < site.Width; dx++)
            {
                _ = _reservations.Remove((anchorX + dx, anchorY + dy));
            }
        }

        return true;
    }
}
