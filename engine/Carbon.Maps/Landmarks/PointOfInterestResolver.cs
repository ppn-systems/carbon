// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Numerics;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Hydrology;
using Carbon.Primitives.Hashing;

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
    private readonly ConcurrentQueue<long> _siteOrder = new();
    private readonly ConcurrentDictionary<long, LandmarkLayout> _layouts = new();
    private readonly ConcurrentQueue<long> _layoutOrder = new();

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
    public PointOfInterest GetSite(int siteX, int siteY)
    {
        long key = ((long)siteX << 32) | (uint)siteY;
        if (_sites.TryGetValue(key, out PointOfInterest cached))
        {
            return cached;
        }

        TrimQueue(_sites, _siteOrder, MaxCachedSites);
        PointOfInterest poi = this.Resolve(siteX, siteY);
        if (_sites.TryAdd(key, poi))
        {
            _siteOrder.Enqueue(key);
        }

        return _sites.TryGetValue(key, out PointOfInterest published) ? published : poi;
    }

    /// <summary>
    /// Finds the point of interest whose footprint covers a world tile.
    /// Footprints never cross site boundaries (<see cref="SiteMargin"/> &gt; max radius), so only the owning site is checked.
    /// </summary>
    public bool TryGetAt(int worldTileX, int worldTileY, out PointOfInterest poi)
    {
        int sx = FloorDiv(worldTileX, SiteSize);
        int sy = FloorDiv(worldTileY, SiteSize);
        PointOfInterest site = this.GetSite(sx, sy);
        if (site.Type != PointOfInterestType.None && site.Contains(worldTileX, worldTileY))
        {
            poi = site;
            return true;
        }

        poi = default;
        return false;
    }

    /// <summary>
    /// Returns whether <paramref name="worldTileX"/>, <paramref name="worldTileY"/> lies on the wooden deck of a Harbor pier.
    /// </summary>
    public bool IsWalkablePierTile(int worldTileX, int worldTileY)
    {
        int sx = FloorDiv(worldTileX, SiteSize);
        int sy = FloorDiv(worldTileY, SiteSize);
        PointOfInterest site = this.GetSite(sx, sy);
        if (site.Type != PointOfInterestType.Harbor)
        {
            return false;
        }

        if (Math.Abs(worldTileX - site.TileX) > 28 || Math.Abs(worldTileY - site.TileY) > 28)
        {
            return false;
        }

        float len = MathF.Sqrt((site.FacingX * site.FacingX) + (site.FacingY * site.FacingY));
        Vector2 sea = len > 0.1f ? new Vector2(site.FacingX / len, site.FacingY / len) : new Vector2(0f, 1f);
        Vector2 side = new(-sea.Y, sea.X);
        Vector2 c = new(site.TileX + 0.5f, site.TileY + 0.5f);
        Vector2 tileCenter = new(worldTileX + 0.5f, worldTileY + 0.5f);

        for (int i = -1; i <= 1; i += 2)
        {
            Vector2 start = c + (side * (i * (52f / 32f)));
            Vector2 end = start + (sea * (site.Radius * 1.55f));
            Vector2 ab = end - start;
            float abLenSq = MathF.Max(ab.LengthSquared(), 1e-5f);
            float t = Math.Clamp(Vector2.Dot(tileCenter - start, ab) / abLenSq, 0f, 1f);
            Vector2 closest = start + (ab * t);
            if (Vector2.DistanceSquared(tileCenter, closest) <= 0.72f * 0.72f)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Gets the structures and fields of a site's point of interest (cached).</summary>
    public LandmarkLayout GetLayout(int siteX, int siteY)
    {
        long key = ((long)siteX << 32) | (uint)siteY;
        if (_layouts.TryGetValue(key, out LandmarkLayout? layout))
        {
            return layout;
        }

        TrimQueue(_layouts, _layoutOrder, MaxCachedSites);
        layout = LandmarkLayout.Build(this.GetSite(siteX, siteY), _tiles);
        if (_layouts.TryAdd(key, layout))
        {
            _layoutOrder.Enqueue(key);
        }

        return _layouts.TryGetValue(key, out LandmarkLayout? published) ? published : layout;
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

        // Starter origin site (0, 0) always searches for a settlement; other sites settle at ~60% density.
        bool isStarterSite = siteX == 0 && siteY == 0;
        if (!isStarterSite && (SpatialHash.Hash(_seed ^ 0x9A0C5E17D2B4F863UL, siteX, siteY) & 0xFF) >= 154)
        {
            return default;
        }

        PointOfInterest best = default;
        float bestScore = 0f;
        int margin = SiteMargin;
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
            byte zoneTier = WorldProgression.GetZoneTier(_seed, x, y);

            // Evaluate cheap non-flat/non-sea gate first, probing sea/flatness only if any rule could plausibly accept.
            float seaX = 0f;
            float seaY = 0f;
            bool nearSea = river <= 12f && tile.HeightLevel == 0 && tile.Elevation < 0.44f && this.TryFindSea(x, y, out seaX, out seaY);
            float seaXOut = seaX;
            float seaYOut = seaY;

            (PointOfInterestType coarseType, _, _) = LandmarkRules.Evaluate(in tile, river, flat: true, nearSea, zoneTier);
            if (coarseType == PointOfInterestType.None)
            {
                continue;
            }

            bool flat = this.IsFlat(x, y, tile.HeightLevel);
            (PointOfInterestType type, float score, int radius) = LandmarkRules.Evaluate(in tile, river, flat, nearSea, zoneTier);
            if (score > bestScore)
            {
                bestScore = score;
                best = type == PointOfInterestType.Harbor
                    ? new PointOfInterest(type, x, y, radius, seaXOut, seaYOut)
                    : new PointOfInterest(type, x, y, radius);
            }
        }

        return this.FinishSite(siteX, siteY, best);
    }

    private PointOfInterest FinishSite(int siteX, int siteY, PointOfInterest best)
    {
        uint finishHash = SpatialHash.Hash32(_seed, siteX, siteY, 0x1F3D5B79UL);
        return best.Type switch
        {
            PointOfInterestType.MountainShrine when (finishHash % 3) != 0 => default,
            PointOfInterestType.BossArena when (finishHash % 6) != 0 => default,
            PointOfInterestType.BanditCamp when (finishHash % 2) != 0 => default,
            PointOfInterestType.AncientRuins when (finishHash % 2) != 0 => default,
            PointOfInterestType.Watchtower when (finishHash % 3) != 0 => default,
            PointOfInterestType.None or
            PointOfInterestType.RiverVillage or
            PointOfInterestType.Harbor or
            PointOfInterestType.MountainShrine or
            PointOfInterestType.LakesideCamp or
            PointOfInterestType.DesertVillage or
            PointOfInterestType.SnowVillage or
            PointOfInterestType.SwampVillage or
            PointOfInterestType.WorldTree or
            PointOfInterestType.MineEntrance or
            PointOfInterestType.AncientRuins or
            PointOfInterestType.BanditCamp or
            PointOfInterestType.Watchtower or
            PointOfInterestType.BossArena => best,
            _ => best,
        };
    }

    private static void TrimQueue<TValue>(ConcurrentDictionary<long, TValue> dict, ConcurrentQueue<long> queue, int max)
    {
        int guard = 0;
        while (dict.Count >= max && guard < 8 && queue.TryDequeue(out long oldest))
        {
            guard++;
            _ = dict.TryRemove(oldest, out _);
        }
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

    // Flat: checks an 8-tile ring + cardinal 1-tile safety neighborhood at the site height level without rivers or cliffs.
    private bool IsFlat(int x, int y, byte level)
    {
        for (int dy = -6; dy <= 6; dy += 2)
        {
            for (int dx = -6; dx <= 6; dx += 2)
            {
                MapTile t = _tiles.SampleTile(x + dx, y + dy);
                if (t.HeightLevel != level || t.IsSolid || t.Biome is BiomeType.ShallowWater or BiomeType.DeepOcean)
                {
                    return false;
                }
            }
        }

        return this.IsDryAtLevel(x + 1, y, level) &&
            this.IsDryAtLevel(x - 1, y, level) &&
            this.IsDryAtLevel(x, y + 1, level) &&
            this.IsDryAtLevel(x, y - 1, level);
    }

    private bool IsDryAtLevel(int x, int y, byte level)
    {
        MapTile t = _tiles.SampleTile(x, y);
        return !t.IsSolid && t.HeightLevel == level && t.Biome is not (BiomeType.ShallowWater or BiomeType.DeepOcean);
    }

    // Finds open sea about 10 tiles away and returns the unit direction towards it.
    private bool TryFindSea(int x, int y, out float dirX, out float dirY)
    {
        dirX = 0f;
        dirY = 0f;
        for (int i = 0; i < s_seaProbes.Length; i++)
        {
            (int ox, int oy) = s_seaProbes[i];
            int sx = x + ox;
            int sy = y + oy;
            MapTile t = _tiles.SampleTile(sx, sy);
            // Open coastal sea only (distinguish from inland DeepOcean river channels whose Elevation == 0.25f).
            if (t.Elevation < 0.22f && t.Biome == BiomeType.DeepOcean)
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
