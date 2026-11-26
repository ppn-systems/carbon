// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Maps.Building;
using Carbon.Maps.Data;

namespace Carbon.Desktop.Abstractions.World;

/// <summary>
/// Client-side store of <see cref="ConstructionMarker"/>s keyed by anchor tile, with construction sites and
/// deconstructions kept apart (a structure can be deconstructed while unrelated sites exist next to it).
/// Not thread-safe: the owning session guards it with its world lock.
/// </summary>
public sealed class ConstructionMarkerBook
{
    private readonly Dictionary<(int X, int Y), ConstructionMarker> _sites = new();
    private readonly Dictionary<(int X, int Y), ConstructionMarker> _deconstructions = new();

    /// <summary>Gets the number of construction sites tracked.</summary>
    public int SiteCount => _sites.Count;

    /// <summary>Gets the number of deconstructions in progress tracked.</summary>
    public int DeconstructionCount => _deconstructions.Count;

    /// <summary>
    /// Applies a server push: <see cref="ConstructionStage.Removed"/> or <see cref="ConstructionStage.None"/> clears the
    /// marker, any other stage adds or replaces it.
    /// </summary>
    /// <param name="marker">The pushed marker.</param>
    public void Apply(in ConstructionMarker marker)
    {
        Dictionary<(int X, int Y), ConstructionMarker> target = marker.IsDeconstruction ? _deconstructions : _sites;
        if (marker.Stage is ConstructionStage.Removed or ConstructionStage.None)
        {
            _ = target.Remove((marker.AnchorX, marker.AnchorY));
        }
        else
        {
            target[(marker.AnchorX, marker.AnchorY)] = marker;
        }
    }

    /// <summary>
    /// Replaces the construction sites anchored in a chunk with the sites of a freshly imported chunk delta.
    /// </summary>
    /// <param name="chunkX">Chunk X.</param>
    /// <param name="chunkY">Chunk Y.</param>
    /// <param name="sites">Delta sites keyed by local anchor index.</param>
    /// <param name="catalog">Catalog used to turn work points into progress.</param>
    public void ReplaceChunkSites(int chunkX, int chunkY, IReadOnlyDictionary<int, ConstructionSite> sites, StructureCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(sites);
        ArgumentNullException.ThrowIfNull(catalog);

        List<(int X, int Y)>? stale = null;
        foreach ((int X, int Y) key in _sites.Keys)
        {
            if (Chunk2D.ToChunkCoord(key.X) == chunkX && Chunk2D.ToChunkCoord(key.Y) == chunkY)
            {
                (stale ??= []).Add(key);
            }
        }

        if (stale is not null)
        {
            foreach ((int X, int Y) key in stale)
            {
                _ = _sites.Remove(key);
            }
        }

        int baseX = chunkX * Chunk2D.Size;
        int baseY = chunkY * Chunk2D.Size;
        foreach (KeyValuePair<int, ConstructionSite> kvp in sites)
        {
            ConstructionSite site = kvp.Value;
            float work = catalog.TryGet(site.Type, out StructureDefinition definition) ? definition.WorkAmount : 0f;
            int ax = baseX + (kvp.Key % Chunk2D.Size);
            int ay = baseY + (kvp.Key / Chunk2D.Size);
            _sites[(ax, ay)] = new ConstructionMarker(
                ax,
                ay,
                site.Width,
                site.Height,
                site.Type,
                site.Rotation,
                site.Stage,
                work > 0f ? Math.Clamp(site.WorkDone / work, 0f, 1f) : 0f,
                IsDeconstruction: false);
        }
    }

    /// <summary>Evaluates whether a tile is covered by a tracked construction site.</summary>
    /// <param name="tileX">World tile X.</param>
    /// <param name="tileY">World tile Y.</param>
    /// <returns><c>true</c> when covered.</returns>
    public bool CoversSite(int tileX, int tileY)
    {
        foreach (ConstructionMarker m in _sites.Values)
        {
            if (tileX >= m.AnchorX && tileX < m.AnchorX + m.Width && tileY >= m.AnchorY && tileY < m.AnchorY + m.Height)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Copies every marker (sites first, then deconstructions) into <paramref name="destination"/>.</summary>
    /// <param name="destination">Cleared and filled.</param>
    public void CopyTo(List<ConstructionMarker> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();
        destination.AddRange(_sites.Values);
        destination.AddRange(_deconstructions.Values);
    }

    /// <summary>Removes every marker (world change, disconnect).</summary>
    public void Clear()
    {
        _sites.Clear();
        _deconstructions.Clear();
    }
}
