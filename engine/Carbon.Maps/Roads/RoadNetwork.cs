// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Carbon.Maps.Data;
using Carbon.Maps.Landmarks;
using Carbon.Maps.Random;

namespace Carbon.Maps.Roads;

/// <summary>A gently curved road between two settlements, as a polyline in tile units.</summary>
/// <param name="Points">Polyline vertices (tile units).</param>
/// <param name="Level">Height level the road runs on; it is not drawn where the ground is at another level.</param>
/// <param name="MinX">Bounding box left.</param>
/// <param name="MinY">Bounding box top.</param>
/// <param name="MaxX">Bounding box right.</param>
/// <param name="MaxY">Bounding box bottom.</param>
public sealed record RoadLink(float[] Points, byte Level, float MinX, float MinY, float MaxX, float MaxY);

/// <summary>
/// Dirt roads linking neighbouring settlements (villages, harbours, mines). Each settlement connects to its
/// nearest settled neighbour (sometimes the second nearest too) along a curve whose bend comes from the
/// seed, so the network is the same for everyone and needs no global pass.
/// </summary>
public sealed class RoadNetwork
{
    /// <summary>Half the road width in tiles.</summary>
    public const float HalfWidth = 0.7f;

    private const int Segments = 24;
    private const int MaxCachedSites = 2048;

    // Sites searched around a settlement for a neighbour, and the longest road in tiles.
    private const int Reach = 1;
    private const int MaxLength = 400;


    private readonly ulong _seed;
    private readonly PointOfInterestResolver _landmarks;
    private readonly ITileSource _tiles;
    private readonly ConcurrentDictionary<long, RoadLink[]> _bySite = new();
    private readonly ConcurrentDictionary<long, (int X, int Y)[]> _choices = new();

    /// <summary>Initializes a new instance of the <see cref="RoadNetwork"/> class.</summary>
    /// <param name="seed">Road stage seed.</param>
    /// <param name="landmarks">Settlement sites.</param>
    /// <param name="tiles">Pure terrain sampler.</param>
    public RoadNetwork(ulong seed, PointOfInterestResolver landmarks, ITileSource tiles)
    {
        _seed = seed;
        _landmarks = landmarks ?? throw new ArgumentNullException(nameof(landmarks));
        _tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
    }

    /// <summary>Returns whether roads connect to this kind of point of interest.</summary>
    /// <param name="type">The kind.</param>
    /// <returns><c>true</c> for settlements and mines.</returns>
    public static bool IsConnected(PointOfInterestType type) => type is
        PointOfInterestType.RiverVillage or PointOfInterestType.DesertVillage or PointOfInterestType.SnowVillage or
        PointOfInterestType.SwampVillage or PointOfInterestType.Harbor or PointOfInterestType.MineEntrance;

    /// <summary>Collects the roads that may cross a tile rectangle.</summary>
    /// <param name="minX">Rectangle left (tiles).</param>
    /// <param name="minY">Rectangle top (tiles).</param>
    /// <param name="maxX">Rectangle right (tiles).</param>
    /// <param name="maxY">Rectangle bottom (tiles).</param>
    /// <param name="result">Receives the roads.</param>
    public void Query(int minX, int minY, int maxX, int maxY, List<RoadLink> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        // A road joins sites at most Reach apart and bows out a little, so it may start Reach + 1 sites away.
        int sx0 = FloorDiv(minX, PointOfInterestResolver.SiteSize) - Reach - 1;
        int sy0 = FloorDiv(minY, PointOfInterestResolver.SiteSize) - Reach - 1;
        int sx1 = FloorDiv(maxX, PointOfInterestResolver.SiteSize) + Reach + 1;
        int sy1 = FloorDiv(maxY, PointOfInterestResolver.SiteSize) + Reach + 1;
        for (int sy = sy0; sy <= sy1; sy++)
        {
            for (int sx = sx0; sx <= sx1; sx++)
            {
                foreach (RoadLink link in this.GetLinks(sx, sy))
                {
                    if (link.MaxX + 1f >= minX && link.MinX - 1f <= maxX + 1 && link.MaxY + 1f >= minY && link.MinY - 1f <= maxY + 1)
                    {
                        result.Add(link);
                    }
                }
            }
        }
    }

    /// <summary>Distance in tiles from a point to a road polyline.</summary>
    /// <param name="link">The road.</param>
    /// <param name="x">X in tiles.</param>
    /// <param name="y">Y in tiles.</param>
    /// <returns>The distance.</returns>
    public static float Distance(RoadLink link, float x, float y)
    {
        ArgumentNullException.ThrowIfNull(link);
        float[] p = link.Points;
        float best = float.MaxValue;
        for (int i = 0; i + 3 < p.Length; i += 2)
        {
            float ax = p[i];
            float ay = p[i + 1];
            float dx = p[i + 2] - ax;
            float dy = p[i + 3] - ay;
            float len2 = (dx * dx) + (dy * dy);
            float t = len2 > 1e-6f ? Math.Clamp((((x - ax) * dx) + ((y - ay) * dy)) / len2, 0f, 1f) : 0f;
            float ex = ax + (dx * t) - x;
            float ey = ay + (dy * t) - y;
            best = MathF.Min(best, (ex * ex) + (ey * ey));
        }

        return MathF.Sqrt(best);
    }

    private RoadLink[] GetLinks(int siteX, int siteY)
    {
        long key = ((long)siteX << 32) | (uint)siteY;
        if (_bySite.TryGetValue(key, out RoadLink[]? links))
        {
            return links;
        }

        if (_bySite.Count > MaxCachedSites)
        {
            _bySite.Clear();
        }

        links = this.BuildLinks(siteX, siteY);
        _bySite[key] = links;
        return links;
    }

    private RoadLink[] BuildLinks(int siteX, int siteY)
    {
        (int X, int Y)[] mine = this.Choose(siteX, siteY);
        if (mine.Length == 0)
        {
            return [];
        }

        PointOfInterest a = _landmarks.GetSite(siteX, siteY);
        byte level = _tiles.SampleTile(a.TileX, a.TileY).HeightLevel;
        List<RoadLink> links = new();
        foreach ((int bx, int by) in mine)
        {
            // A pair that chose each other is built once, by the site that sorts first.
            if (Array.IndexOf(this.Choose(bx, by), (siteX, siteY)) >= 0 && (by < siteY || (by == siteY && bx < siteX)))
            {
                continue;
            }

            PointOfInterest b = _landmarks.GetSite(bx, by);
            ulong pair = SpatialHash.Hash(_seed ^ 0x40AD5EEDUL, siteX + bx, siteY + by) ^ (ulong)(((siteX - bx) * 7) + (siteY - by));
            links.Add(this.Curve(a, b, level, pair));
        }

        return [.. links];
    }

    // Each settlement links to its nearest settled neighbour on the same height level within one site, and
    // sometimes to the second nearest too, which closes loops between villages.
    private (int X, int Y)[] Choose(int siteX, int siteY)
    {
        long key = ((long)siteX << 32) | (uint)siteY;
        if (_choices.TryGetValue(key, out (int X, int Y)[]? cached))
        {
            return cached;
        }

        if (_choices.Count > MaxCachedSites)
        {
            _choices.Clear();
        }

        (int X, int Y)[] result = this.ChooseCore(siteX, siteY);
        _choices[key] = result;
        return result;
    }

    private (int X, int Y)[] ChooseCore(int siteX, int siteY)
    {
        List<(int X, int Y)> chosen = new(2);
        PointOfInterest a = _landmarks.GetSite(siteX, siteY);
        if (!IsConnected(a.Type))
        {
            return [];
        }

        byte level = _tiles.SampleTile(a.TileX, a.TileY).HeightLevel;
        (int X, int Y) best = default;
        (int X, int Y) second = default;
        long bestD = long.MaxValue;
        long secondD = long.MaxValue;
        for (int dy = -Reach; dy <= Reach; dy++)
        {
            for (int dx = -Reach; dx <= Reach; dx++)
            {
                if (dx == 0 && dy == 0)
                {
                    continue;
                }

                PointOfInterest b = _landmarks.GetSite(siteX + dx, siteY + dy);
                if (!IsConnected(b.Type) || _tiles.SampleTile(b.TileX, b.TileY).HeightLevel != level)
                {
                    continue;
                }

                long ex = b.TileX - a.TileX;
                long ey = b.TileY - a.TileY;
                long d = (ex * ex) + (ey * ey);
                if (d > MaxLength * MaxLength)
                {
                    continue;
                }

                if (d < bestD)
                {
                    (second, secondD) = (best, bestD);
                    (best, bestD) = ((siteX + dx, siteY + dy), d);
                }
                else if (d < secondD)
                {
                    (second, secondD) = ((siteX + dx, siteY + dy), d);
                }
            }
        }

        if (bestD != long.MaxValue)
        {
            chosen.Add(best);
        }

        if (secondD != long.MaxValue && SpatialHash.Hash(_seed, siteX, siteY) % 3 == 0)
        {
            chosen.Add(second);
        }

        return [.. chosen];
    }

    // A quadratic Bezier from centre to centre, bent sideways by up to a fifth of its length.
    private RoadLink Curve(in PointOfInterest a, in PointOfInterest b, byte level, ulong hash)
    {
        float ax = a.TileX + 0.5f;
        float ay = a.TileY + 0.5f;
        float bx = b.TileX + 0.5f;
        float by = b.TileY + 0.5f;
        float mx = (ax + bx) * 0.5f;
        float my = (ay + by) * 0.5f;
        float bend = (((hash & 0xFFFF) / 65535f) - 0.5f) * 0.4f;
        float cx = mx - ((by - ay) * bend);
        float cy = my + ((bx - ax) * bend);

        float[] points = new float[(Segments + 1) * 2];
        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;
        for (int i = 0; i <= Segments; i++)
        {
            float t = i / (float)Segments;
            float u = 1f - t;
            float x = (u * u * ax) + (2f * u * t * cx) + (t * t * bx);
            float y = (u * u * ay) + (2f * u * t * cy) + (t * t * by);
            points[i * 2] = x;
            points[(i * 2) + 1] = y;
            minX = MathF.Min(minX, x);
            minY = MathF.Min(minY, y);
            maxX = MathF.Max(maxX, x);
            maxY = MathF.Max(maxY, y);
        }

        return new RoadLink(points, level, minX, minY, maxX, maxY);
    }

    private static int FloorDiv(int a, int b) => a >= 0 ? a / b : ((a + 1) / b) - 1;
}
