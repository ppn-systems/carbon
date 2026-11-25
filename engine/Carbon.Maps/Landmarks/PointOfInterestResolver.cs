// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Hydrology;
using Carbon.Maps.Random;

namespace Carbon.Maps.Landmarks;

/// <summary>
/// Places points of interest where people would actually settle, from the generated terrain:
/// harbours where a river meets the coast, villages on flat fertile lowland near (not on) a river,
/// lakeside camps on calm shores, and shrines on summits.
/// </summary>
/// <remarks>
/// The world is divided into sites of <see cref="SiteSize"/> tiles. Each site examines a fixed set of
/// hashed candidate tiles, scores every candidate for every kind, and keeps the best one, so the result
/// is deterministic and independent of the order in which chunks are generated. Sites hold at most one
/// point of interest, and only some sites hold one, which keeps settlements apart.
/// </remarks>
public sealed class PointOfInterestResolver
{
    /// <summary>Edge of a site in tiles.</summary>
    public const int SiteSize = 224;

    /// <summary>
    /// Distance from a site's edge to any point of interest centre. Larger than every footprint
    /// (radius ≤ 16 tiles, structures inside it), so a point of interest never reaches into another site.
    /// </summary>
    public const int SiteMargin = 24;

    private const int Candidates = 40;
    private const int MaxCachedSites = 2048;

    // Eight probes ~10 tiles out (integer offsets keep the result identical on every platform).
    private static readonly (int X, int Y)[] s_seaProbes =
    [
        (10, 0), (7, 7), (0, 10), (-7, 7), (-10, 0), (-7, -7), (0, -10), (7, -7),
    ];

    private readonly ulong _seed;
    private readonly ITileSource _tiles;
    private readonly IRiverDistanceSource _rivers;
    private readonly ConcurrentDictionary<long, PointOfInterest> _sites = new();
    private readonly ConcurrentDictionary<long, LandmarkLayout> _layouts = new();

    /// <summary>Initializes a new instance of the <see cref="PointOfInterestResolver"/> class.</summary>
    /// <param name="seed">World seed.</param>
    /// <param name="tiles">Pure tile sampler.</param>
    /// <param name="rivers">Width-scaled distance to rivers and lakes.</param>
    public PointOfInterestResolver(ulong seed, ITileSource tiles, IRiverDistanceSource rivers)
    {
        _seed = seed;
        _tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
        _rivers = rivers ?? throw new ArgumentNullException(nameof(rivers));
    }

    /// <summary>Gets the point of interest of a site, or one with <see cref="PointOfInterestType.None"/>.</summary>
    /// <param name="siteX">Site X.</param>
    /// <param name="siteY">Site Y.</param>
    /// <returns>The site's point of interest.</returns>
    public PointOfInterest GetSite(int siteX, int siteY)
    {
        long key = ((long)siteX << 32) | (uint)siteY;
        if (_sites.TryGetValue(key, out PointOfInterest cached))
        {
            return cached;
        }

        if (_sites.Count > MaxCachedSites)
        {
            _sites.Clear();
        }

        PointOfInterest poi = this.Resolve(siteX, siteY);
        _sites[key] = poi;
        return poi;
    }

    /// <summary>
    /// Finds the point of interest whose footprint covers a world tile.
    /// </summary>
    /// <param name="worldTileX">World tile X.</param>
    /// <param name="worldTileY">World tile Y.</param>
    /// <param name="poi">The point of interest.</param>
    /// <returns><c>true</c> when the tile is inside one.</returns>
    public bool TryGetAt(int worldTileX, int worldTileY, out PointOfInterest poi)
    {
        int sx = FloorDiv(worldTileX, SiteSize);
        int sy = FloorDiv(worldTileY, SiteSize);
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                PointOfInterest site = this.GetSite(sx + dx, sy + dy);
                if (site.Type != PointOfInterestType.None && site.Contains(worldTileX, worldTileY))
                {
                    poi = site;
                    return true;
                }
            }
        }

        poi = default;
        return false;
    }

    /// <summary>Gets the structures and fields of a site's point of interest (cached).</summary>
    /// <param name="siteX">Site X.</param>
    /// <param name="siteY">Site Y.</param>
    /// <returns>The layout, empty when the site has no point of interest.</returns>
    public LandmarkLayout GetLayout(int siteX, int siteY)
    {
        long key = ((long)siteX << 32) | (uint)siteY;
        if (_layouts.TryGetValue(key, out LandmarkLayout? layout))
        {
            return layout;
        }

        if (_layouts.Count > MaxCachedSites)
        {
            _layouts.Clear();
        }

        layout = LandmarkLayout.Build(this.GetSite(siteX, siteY), _tiles);
        _layouts[key] = layout;
        return layout;
    }

    /// <summary>
    /// Returns whether a structure (house, well, tent, ...) blocks a world tile. Used for navigation.
    /// </summary>
    /// <param name="tileX">World tile X.</param>
    /// <param name="tileY">World tile Y.</param>
    /// <returns><c>true</c> when the tile is blocked.</returns>
    public bool IsTileBlocked(int tileX, int tileY)
    {
        // Footprints never leave their own site (see SiteMargin), so only that site needs checking.
        int sx = FloorDiv(tileX, SiteSize);
        int sy = FloorDiv(tileY, SiteSize);
        PointOfInterest site = this.GetSite(sx, sy);
        return site.Type != PointOfInterestType.None &&
            Math.Abs(site.TileX - tileX) <= site.Radius + 4 &&
            Math.Abs(site.TileY - tileY) <= site.Radius + 4 &&
            this.GetLayout(sx, sy).BlocksTile(tileX, tileY);
    }

    private PointOfInterest Resolve(int siteX, int siteY)
    {
        // About one site in forty may host a World Tree, if it has a quiet, flat, green spot for it.
        if (SpatialHash.Hash(_seed ^ 0x77E5A1C3D9F0B246UL, siteX, siteY) % 40 == 0 && this.TryPlaceWorldTree(siteX, siteY, out PointOfInterest tree))
        {
            return tree;
        }

        // About half of the sites are settled at all.
        if ((SpatialHash.Hash(_seed ^ 0x9A0C5E17D2B4F863UL, siteX, siteY) & 0xFF) >= 128)
        {
            return default;
        }

        PointOfInterest best = default;
        float bestScore = 0f;
        int margin = SiteMargin; // keep footprints inside the site so neighbouring sites never overlap
        for (int i = 0; i < Candidates; i++)
        {
            ulong h = SpatialHash.Hash(_seed ^ 0x47C1E3A5B9D2F608UL, (siteX * Candidates) + i, siteY);
            int x = (siteX * SiteSize) + margin + (int)(h % (ulong)(SiteSize - (2 * margin)));
            int y = (siteY * SiteSize) + margin + (int)((h >> 24) % (ulong)(SiteSize - (2 * margin)));
            MapTile tile = _tiles.SampleTile(x, y);
            if (tile.IsSolid || tile.Biome is BiomeType.DeepOcean or BiomeType.ShallowWater)
            {
                continue;
            }

            float river = _rivers.GetDistance(x, y);
            bool nearSea = this.TryFindSea(x, y, out float seaX, out float seaY);
            (PointOfInterestType type, float score, int radius) = LandmarkRules.Evaluate(tile, river, this.IsFlat(x, y, tile.HeightLevel), nearSea);
            if (score > bestScore)
            {
                bestScore = score;
                best = type == PointOfInterestType.Harbor
                    ? new PointOfInterest(type, x, y, radius, seaX, seaY)
                    : new PointOfInterest(type, x, y, radius);
            }
        }

        return this.FinishSite(siteX, siteY, best);
    }

    private PointOfInterest FinishSite(int siteX, int siteY, PointOfInterest best)
    {
        // Only one mountain site in three keeps its shrine, so summits are not all crowned.
        if (best.Type == PointOfInterestType.MountainShrine &&
            (SpatialHash.Hash(_seed ^ 0x1F3D5B7997B5D3F1UL, siteX, siteY) % 3) != 0)
        {
            return default;
        }

        return best;
    }

    // A World Tree needs flat lowland or plateau in a green biome, well away from rivers; the candidate
    // farthest from water wins.
    private bool TryPlaceWorldTree(int siteX, int siteY, out PointOfInterest tree)
    {
        tree = default;
        float best = 0f;
        for (int i = 0; i < Candidates; i++)
        {
            ulong h = SpatialHash.Hash(_seed ^ 0x47C1E3A5B9D2F608UL, (siteX * Candidates) + i, siteY);
            int x = (siteX * SiteSize) + SiteMargin + (int)(h % (SiteSize - (2 * SiteMargin)));
            int y = (siteY * SiteSize) + SiteMargin + (int)((h >> 24) % (SiteSize - (2 * SiteMargin)));
            MapTile tile = _tiles.SampleTile(x, y);
            if (tile.IsSolid || tile.HeightLevel > 1 ||
                tile.Biome is not (BiomeType.LushPlains or BiomeType.AncientForest or BiomeType.BlossomWoods or BiomeType.TropicalJungle))
            {
                continue;
            }

            float river = _rivers.GetDistance(x, y);
            float score = MathF.Min(river, 30f);
            if (river > 8f && score > best && this.IsFlat(x, y, tile.HeightLevel))
            {
                best = score;
                tree = new PointOfInterest(PointOfInterestType.WorldTree, x, y, 14);
            }
        }

        return tree.Type == PointOfInterestType.WorldTree;
    }

    // Flat: the 5x5 tiles around the site share its height level and none of them is water.
    private bool IsFlat(int x, int y, byte level)
    {
        for (int dy = -4; dy <= 4; dy += 2)
        {
            for (int dx = -4; dx <= 4; dx += 2)
            {
                MapTile t = _tiles.SampleTile(x + dx, y + dy);
                if (t.HeightLevel != level || t.IsSolid || t.Biome is BiomeType.ShallowWater or BiomeType.DeepOcean)
                {
                    return false;
                }
            }
        }

        return true;
    }

    // Finds open sea about 10 tiles away and returns the unit direction towards it.
    private bool TryFindSea(int x, int y, out float dirX, out float dirY)
    {
        dirX = 0f;
        dirY = 0f;
        foreach ((int ox, int oy) in s_seaProbes)
        {
            int sx = x + ox;
            int sy = y + oy;
            MapTile t = _tiles.SampleTile(sx, sy);
            if (t.Biome is BiomeType.DeepOcean || (t.Biome is BiomeType.ShallowWater && t.Elevation < 0.30f && _rivers.GetDistance(sx, sy) > WorldConstants.RiverChannel))
            {
                dirX += ox;
                dirY += oy;
            }
        }

        float len = MathF.Sqrt((dirX * dirX) + (dirY * dirY));
        if (len < 1e-3f)
        {
            return false;
        }

        dirX /= len;
        dirY /= len;
        return true;
    }

    private static int FloorDiv(int a, int b) => a >= 0 ? a / b : ((a + 1) / b) - 1;
}
