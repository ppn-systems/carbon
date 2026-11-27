// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Hydrology;
using Carbon.Maps.Landmarks;
using Carbon.Maps.Topography;
using Carbon.Primitives.Hashing;

namespace Carbon.Maps.Roads;

/// <summary>A terrain-routed road or bridge segment between settlements, as a polyline in tile units.</summary>
/// <param name="Points">Polyline vertices (tile units).</param>
/// <param name="Level">Height level the road runs on.</param>
/// <param name="MinX">Bounding box left.</param>
/// <param name="MinY">Bounding box top.</param>
/// <param name="MaxX">Bounding box right.</param>
/// <param name="MaxY">Bounding box bottom.</param>
/// <param name="IsBridge">Whether this span crosses river/inland water as a wooden bridge.</param>
public sealed record RoadLink(
    float[] Points,
    byte Level,
    float MinX,
    float MinY,
    float MaxX,
    float MaxY,
    bool IsBridge = false);

/// <summary>
/// Terrain-aware coarse A* road network connecting settlements, harbours, frontier towers, ruins, and highland mines.
/// Avoids deep oceans, constructs wooden bridges across rivers, and carves road ramps across single-step cliffs.
/// </summary>
public sealed class RoadNetwork
{
    /// <summary>Half the road width in tiles.</summary>
    public const float HalfWidth = 0.7f;

    /// <summary>Half the wooden bridge width in tiles.</summary>
    public const float BridgeHalfWidth = 0.85f;

    private const int MaxCachedSites = 2048;
    private const int Reach = 1;
    private const int MaxLength = 420;
    private const int CoarseStep = 4;
    private const int MaxGridDim = 128;

    private readonly record struct RoadSiteData(
        RoadLink[] Links,
        Dictionary<long, RampDirection> Ramps);

    private readonly ulong _seed;
    private readonly PointOfInterestResolver _landmarks;
    private readonly ITileSource _tiles;
    private readonly IRiverDistanceSource _rivers;
    private readonly ConcurrentDictionary<long, RoadSiteData> _bySite = new();
    private readonly ConcurrentQueue<long> _siteOrder = new();
    private readonly ConcurrentDictionary<long, (int X, int Y)[]> _choices = new();
    private readonly ConcurrentQueue<long> _choiceOrder = new();

    /// <summary>Initializes a new instance of the <see cref="RoadNetwork"/> class.</summary>
    public RoadNetwork(ulong seed, PointOfInterestResolver landmarks, ITileSource tiles, IRiverDistanceSource? rivers = null)
    {
        _seed = seed;
        _landmarks = landmarks ?? throw new ArgumentNullException(nameof(landmarks));
        _tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
        _rivers = rivers!;
    }

    /// <summary>Returns whether roads connect to this kind of point of interest.</summary>
    /// <remarks>Villages are not connected (ADR-027): they keep their own streets and no road leads in or out.</remarks>
    public static bool IsConnected(PointOfInterestType type) => type is
        PointOfInterestType.Harbor or PointOfInterestType.MineEntrance or
        PointOfInterestType.Watchtower or PointOfInterestType.AncientRuins;

    /// <summary>Collects the roads and bridge spans that may cross a tile rectangle.</summary>
    public void Query(int minX, int minY, int maxX, int maxY, List<RoadLink> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        int sx0 = FloorDiv(minX, PointOfInterestResolver.SiteSize) - Reach - 1;
        int sy0 = FloorDiv(minY, PointOfInterestResolver.SiteSize) - Reach - 1;
        int sx1 = FloorDiv(maxX, PointOfInterestResolver.SiteSize) + Reach + 1;
        int sy1 = FloorDiv(maxY, PointOfInterestResolver.SiteSize) + Reach + 1;
        for (int sy = sy0; sy <= sy1; sy++)
        {
            for (int sx = sx0; sx <= sx1; sx++)
            {
                RoadLink[] links = this.GetSiteData(sx, sy).Links;
                for (int i = 0; i < links.Length; i++)
                {
                    RoadLink link = links[i];
                    if (link.MaxX + 1.5f >= minX && link.MinX - 1.5f <= maxX + 1 &&
                        link.MaxY + 1.5f >= minY && link.MinY - 1.5f <= maxY + 1)
                    {
                        result.Add(link);
                    }
                }
            }
        }
    }

    /// <summary>Checks whether a road carved a ramp incline on <paramref name="worldX"/>, <paramref name="worldY"/>.</summary>
    public bool TryGetRoadRamp(int worldX, int worldY, out RampDirection ramp)
    {
        int sx0 = FloorDiv(worldX, PointOfInterestResolver.SiteSize) - Reach - 1;
        int sy0 = FloorDiv(worldY, PointOfInterestResolver.SiteSize) - Reach - 1;
        int sx1 = FloorDiv(worldX, PointOfInterestResolver.SiteSize) + Reach + 1;
        int sy1 = FloorDiv(worldY, PointOfInterestResolver.SiteSize) + Reach + 1;
        long packed = PackTile(worldX, worldY);

        for (int sy = sy0; sy <= sy1; sy++)
        {
            for (int sx = sx0; sx <= sx1; sx++)
            {
                Dictionary<long, RampDirection> ramps = this.GetSiteData(sx, sy).Ramps;
                if (ramps.Count > 0 && ramps.TryGetValue(packed, out ramp))
                {
                    return true;
                }
            }
        }

        ramp = RampDirection.None;
        return false;
    }

    /// <summary>Evaluates whether <paramref name="worldX"/>, <paramref name="worldY"/> lies on a dry road or bridge tile outside loaded chunks.</summary>
    public bool IsRoadOrBridgeAtTile(int worldX, int worldY, bool bridgeOnly)
    {
        int sx0 = FloorDiv(worldX, PointOfInterestResolver.SiteSize) - Reach - 1;
        int sy0 = FloorDiv(worldY, PointOfInterestResolver.SiteSize) - Reach - 1;
        int sx1 = FloorDiv(worldX, PointOfInterestResolver.SiteSize) + Reach + 1;
        int sy1 = FloorDiv(worldY, PointOfInterestResolver.SiteSize) + Reach + 1;
        float px = worldX + 0.5f;
        float py = worldY + 0.5f;

        for (int sy = sy0; sy <= sy1; sy++)
        {
            for (int sx = sx0; sx <= sx1; sx++)
            {
                RoadLink[] links = this.GetSiteData(sx, sy).Links;
                for (int i = 0; i < links.Length; i++)
                {
                    RoadLink link = links[i];
                    if (link.IsBridge != bridgeOnly)
                    {
                        continue;
                    }

                    float limit = bridgeOnly ? BridgeHalfWidth : HalfWidth;
                    if (px >= link.MinX - 1f && px <= link.MaxX + 1f &&
                        py >= link.MinY - 1f && py <= link.MaxY + 1f &&
                        Distance(link, px, py) <= limit)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Marks every tile of a rectangle that a dry road or a bridge covers: the same rule as
    /// <see cref="IsRoadOrBridgeAtTile"/> (tile centre within the half width of a span), for a whole area at once.
    /// </summary>
    /// <param name="minX">Left tile of the rectangle.</param>
    /// <param name="minY">Top tile of the rectangle.</param>
    /// <param name="width">Width in tiles.</param>
    /// <param name="height">Height in tiles.</param>
    /// <param name="output">Receives <c>true</c> for road tiles, row-major, <paramref name="width"/> per row (not cleared).</param>
    public void Rasterize(int minX, int minY, int width, int height, Span<bool> output)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(output.Length, width * height);
        List<RoadLink> links = [];
        this.Query(minX, minY, minX + width - 1, minY + height - 1, links);
        foreach (RoadLink link in links)
        {
            float limit = link.IsBridge ? BridgeHalfWidth : HalfWidth;
            float[] p = link.Points;
            for (int i = 0; i + 3 < p.Length; i += 2)
            {
                float ax = p[i];
                float ay = p[i + 1];
                float bx = p[i + 2];
                float by = p[i + 3];
                int x0 = Math.Max(minX, (int)MathF.Floor(MathF.Min(ax, bx) - limit - 0.5f));
                int x1 = Math.Min(minX + width - 1, (int)MathF.Floor(MathF.Max(ax, bx) + limit + 0.5f));
                int y0 = Math.Max(minY, (int)MathF.Floor(MathF.Min(ay, by) - limit - 0.5f));
                int y1 = Math.Min(minY + height - 1, (int)MathF.Floor(MathF.Max(ay, by) + limit + 0.5f));
                float dx = bx - ax;
                float dy = by - ay;
                float len2 = (dx * dx) + (dy * dy);
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        float px = x + 0.5f;
                        float py = y + 0.5f;
                        float t = len2 > 1e-6f ? Math.Clamp((((px - ax) * dx) + ((py - ay) * dy)) / len2, 0f, 1f) : 0f;
                        float ex = ax + (dx * t) - px;
                        float ey = ay + (dy * t) - py;
                        if (MathF.Sqrt((ex * ex) + (ey * ey)) <= limit)
                        {
                            output[((y - minY) * width) + (x - minX)] = true;
                        }
                    }
                }
            }
        }
    }

    /// <summary>Distance in tiles from a point to a road polyline.</summary>
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

    private RoadSiteData GetSiteData(int siteX, int siteY)
    {
        long key = ((long)siteX << 32) | (uint)siteY;
        if (_bySite.TryGetValue(key, out RoadSiteData cached))
        {
            return cached;
        }

        TrimQueue(_bySite, _siteOrder, MaxCachedSites);
        RoadSiteData built = this.BuildSiteData(siteX, siteY);
        if (_bySite.TryAdd(key, built))
        {
            _siteOrder.Enqueue(key);
        }

        return _bySite.TryGetValue(key, out RoadSiteData published) ? published : built;
    }

    private RoadSiteData BuildSiteData(int siteX, int siteY)
    {
        (int X, int Y)[] mine = this.Choose(siteX, siteY);
        if (mine.Length == 0)
        {
            return new RoadSiteData([], new Dictionary<long, RampDirection>());
        }

        PointOfInterest a = _landmarks.GetSite(siteX, siteY);
        List<RoadLink> links = new();
        Dictionary<long, RampDirection> ramps = new();

        foreach ((int bx, int by) in mine)
        {
            if (Array.IndexOf(this.Choose(bx, by), (siteX, siteY)) >= 0 && (by < siteY || (by == siteY && bx < siteX)))
            {
                continue;
            }

            PointOfInterest b = _landmarks.GetSite(bx, by);
            this.RouteBetweenSites(a, b, links, ramps);
        }

        return new RoadSiteData([.. links], ramps);
    }

    private (int X, int Y)[] Choose(int siteX, int siteY)
    {
        long key = ((long)siteX << 32) | (uint)siteY;
        if (_choices.TryGetValue(key, out (int X, int Y)[]? cached))
        {
            return cached;
        }

        TrimQueue(_choices, _choiceOrder, MaxCachedSites);
        (int X, int Y)[] result = this.ChooseCore(siteX, siteY);
        if (_choices.TryAdd(key, result))
        {
            _choiceOrder.Enqueue(key);
        }

        return _choices.TryGetValue(key, out (int X, int Y)[]? published) ? published : result;
    }

    private (int X, int Y)[] ChooseCore(int siteX, int siteY)
    {
        List<(int X, int Y)> chosen = new(2);
        PointOfInterest a = _landmarks.GetSite(siteX, siteY);
        if (!IsConnected(a.Type))
        {
            return [];
        }

        byte levelA = _tiles.SampleTile(a.TileX, a.TileY).HeightLevel;
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
                if (!IsConnected(b.Type))
                {
                    continue;
                }

                byte levelB = _tiles.SampleTile(b.TileX, b.TileY).HeightLevel;
                if (Math.Abs(levelA - levelB) > 1)
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

    private void RouteBetweenSites(
        in PointOfInterest a,
        in PointOfInterest b,
        List<RoadLink> outLinks,
        Dictionary<long, RampDirection> outRamps)
    {
        (int ax, int ay) = this.ResolveGateTile(a, b.TileX, b.TileY);
        (int bx, int by) = this.ResolveGateTile(b, a.TileX, a.TileY);

        int pad = 40;
        int minWX = Math.Min(ax, bx) - pad;
        int minWY = Math.Min(ay, by) - pad;
        int maxWX = Math.Max(ax, bx) + pad;
        int maxWY = Math.Max(ay, by) + pad;

        int gx0 = FloorDiv(minWX, CoarseStep);
        int gy0 = FloorDiv(minWY, CoarseStep);
        int gx1 = FloorDiv(maxWX, CoarseStep);
        int gy1 = FloorDiv(maxWY, CoarseStep);
        int width = Math.Clamp(gx1 - gx0 + 1, 4, MaxGridDim);
        int height = Math.Clamp(gy1 - gy0 + 1, 4, MaxGridDim);
        int total = width * height;

        int startX = Math.Clamp(FloorDiv(ax, CoarseStep) - gx0, 0, width - 1);
        int startY = Math.Clamp(FloorDiv(ay, CoarseStep) - gy0, 0, height - 1);
        int goalX = Math.Clamp(FloorDiv(bx, CoarseStep) - gx0, 0, width - 1);
        int goalY = Math.Clamp(FloorDiv(by, CoarseStep) - gy0, 0, height - 1);
        int startIdx = (startY * width) + startX;
        int goalIdx = (goalY * width) + goalX;

        byte[] kind = ArrayPool<byte>.Shared.Rent(total); // 0=uninit, 1=land, 2=riverBridgeWater, 3=blockedSea
        byte[] elevLevel = ArrayPool<byte>.Shared.Rent(total);
        float[] gScore = ArrayPool<float>.Shared.Rent(total);
        int[] cameFrom = ArrayPool<int>.Shared.Rent(total);
        bool[] closed = ArrayPool<bool>.Shared.Rent(total);

        try
        {
            Array.Clear(kind, 0, total);
            Array.Fill(gScore, float.PositiveInfinity, 0, total);
            Array.Fill(cameFrom, -1, 0, total);
            Array.Clear(closed, 0, total);

            PriorityQueue<int, float> open = new(Math.Min(total, 512));
            gScore[startIdx] = 0f;
            open.Enqueue(startIdx, Heuristic(startX, startY, goalX, goalY));
            this.EnsureCell(startIdx, width, gx0, gy0, kind, elevLevel);
            kind[startIdx] = 1; // Guarantee start & goal gates can launch A*

            this.EnsureCell(goalIdx, width, gx0, gy0, kind, elevLevel);
            if (kind[goalIdx] == 3)
            {
                return;
            }

            bool found = false;
            int[] dxs = [1, -1, 0, 0];
            int[] dys = [0, 0, 1, -1];

            while (open.TryDequeue(out int current, out _))
            {
                if (closed[current])
                {
                    continue;
                }

                if (current == goalIdx)
                {
                    found = true;
                    break;
                }

                closed[current] = true;
                int cx = current % width;
                int cy = current / width;
                byte hCur = elevLevel[current];
                byte kCur = kind[current];

                for (int d = 0; d < 4; d++)
                {
                    int nx = cx + dxs[d];
                    int ny = cy + dys[d];
                    if ((uint)nx >= (uint)width || (uint)ny >= (uint)height)
                    {
                        continue;
                    }

                    int ni = (ny * width) + nx;
                    if (closed[ni])
                    {
                        continue;
                    }

                    this.EnsureCell(ni, width, gx0, gy0, kind, elevLevel);
                    byte kN = kind[ni];
                    if (kN == 3)
                    {
                        continue;
                    }

                    byte hN = elevLevel[ni];
                    int dh = Math.Abs(hN - hCur);
                    if (dh > 1)
                    {
                        continue;
                    }

                    // Never change height levels directly from a bridge water tile.
                    if (dh == 1 && (kCur == 2 || kN == 2))
                    {
                        continue;
                    }

                    float stepCost = CoarseStep;
                    if (kN == 2)
                    {
                        stepCost += CoarseStep * 5.0f; // Bridge cost encourages minimal river crossings
                    }

                    if (dh == 1)
                    {
                        stepCost += CoarseStep * 4.0f; // Cliff climb cost prefers staying level unless needed
                    }

                    float tentativeG = gScore[current] + stepCost;
                    if (tentativeG < gScore[ni])
                    {
                        cameFrom[ni] = current;
                        gScore[ni] = tentativeG;
                        open.Enqueue(ni, tentativeG + Heuristic(nx, ny, goalX, goalY));
                    }
                }
            }

            if (!found)
            {
                return;
            }

            List<int> cellPath = new(128);
            for (int c = goalIdx; c >= 0; c = cameFrom[c])
            {
                cellPath.Add(c);
            }

            cellPath.Reverse();
            this.BuildLinksFromCoarsePath(ax, ay, bx, by, cellPath, width, gx0, gy0, outLinks, outRamps);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(kind);
            ArrayPool<byte>.Shared.Return(elevLevel);
            ArrayPool<float>.Shared.Return(gScore);
            ArrayPool<int>.Shared.Return(cameFrom);
            ArrayPool<bool>.Shared.Return(closed);
        }
    }

    private void BuildLinksFromCoarsePath(
        int ax,
        int ay,
        int bx,
        int by,
        List<int> cellPath,
        int width,
        int gx0,
        int gy0,
        List<RoadLink> outLinks,
        Dictionary<long, RampDirection> outRamps)
    {
        if (cellPath.Count == 0)
        {
            return;
        }

        // Convert coarse grid waypoints into 1-tile steps so river banks and height-level transitions are crisp.
        List<(int X, int Y, byte Level, bool IsWater)> finePoints = new(cellPath.Count * 2);
        int prevX = ax;
        int prevY = ay;
        this.SamplePoint(prevX, prevY, out byte pLevel, out bool pWater);
        finePoints.Add((prevX, prevY, pLevel, pWater));

        for (int i = 1; i < cellPath.Count; i++)
        {
            int idx = cellPath[i];
            int targetX = (i == cellPath.Count - 1) ? bx : ((gx0 + (idx % width)) * CoarseStep) + (CoarseStep / 2);
            int targetY = (i == cellPath.Count - 1) ? by : ((gy0 + (idx / width)) * CoarseStep) + (CoarseStep / 2);

            int sx = prevX;
            int sy = prevY;
            while (sx != targetX || sy != targetY)
            {
                if (Math.Abs(targetX - sx) >= Math.Abs(targetY - sy))
                {
                    sx += Math.Sign(targetX - sx);
                }
                else
                {
                    sy += Math.Sign(targetY - sy);
                }

                this.SamplePoint(sx, sy, out byte curLevel, out bool curWater);
                if (Math.Abs(curLevel - pLevel) == 1 && !pWater && !curWater)
                {
                    RegisterRoadRamp(prevX, prevY, pLevel, sx, sy, curLevel, outRamps);
                }

                finePoints.Add((sx, sy, curLevel, curWater));
                prevX = sx;
                prevY = sy;
                pLevel = curLevel;
                pWater = curWater;
            }
        }

        // Emit homogeneous dry RoadLinks (splitting at height changes and river bridges) plus Bridge links over water.
        List<float> currentCoords = new(64);
        byte activeLevel = finePoints[0].Level;
        bool activeBridge = finePoints[0].IsWater;

        for (int i = 0; i < finePoints.Count; i++)
        {
            (int wx, int wy, byte lvl, bool water) = finePoints[i];
            if (lvl != activeLevel || water != activeBridge)
            {
                if (water && currentCoords.Count >= 2)
                {
                    // Extend bridge abutment slightly onto the departure dry edge.
                    currentCoords.Add(wx + 0.5f);
                    currentCoords.Add(wy + 0.5f);
                }

                EmitPolyline(currentCoords, activeLevel, activeBridge, outLinks);
                currentCoords.Clear();

                if (!water && activeBridge && i > 0)
                {
                    (int pwx, int pwy, _, _) = finePoints[i - 1];
                    currentCoords.Add(pwx + 0.5f);
                    currentCoords.Add(pwy + 0.5f);
                }

                activeLevel = lvl;
                activeBridge = water;
            }

            currentCoords.Add(wx + 0.5f);
            currentCoords.Add(wy + 0.5f);
        }

        EmitPolyline(currentCoords, activeLevel, activeBridge, outLinks);
    }

    private void SamplePoint(int x, int y, out byte level, out bool isWater)
    {
        MapTile t = _tiles.SampleTile(x, y);
        level = t.HeightLevel;
        isWater = t.Biome is BiomeType.ShallowWater or BiomeType.DeepOcean;
    }

    private static void RegisterRoadRamp(
        int x0,
        int y0,
        byte level0,
        int x1,
        int y1,
        byte level1,
        Dictionary<long, RampDirection> outRamps)
    {
        bool climbing = level1 > level0;
        int highX = climbing ? x1 : x0;
        int highY = climbing ? y1 : y0;
        int lowX = climbing ? x0 : x1;
        int lowY = climbing ? y0 : y1;
        int dx = highX - lowX;
        int dy = highY - lowY;

        RampDirection highDir = dy < 0
            ? RampDirection.South
            : dy > 0
                ? RampDirection.North
                : dx > 0
                    ? RampDirection.West
                    : RampDirection.East;

        outRamps[PackTile(highX, highY)] = highDir;
    }

    private static void EmitPolyline(List<float> coords, byte level, bool isBridge, List<RoadLink> outLinks)
    {
        if (coords.Count < 4)
        {
            return;
        }

        // Simplify collinear consecutive points so distance checks remain fast.
        List<float> simplified = new(coords.Count);
        simplified.Add(coords[0]);
        simplified.Add(coords[1]);
        for (int i = 2; i + 3 < coords.Count; i += 2)
        {
            float ax = simplified[^2];
            float ay = simplified[^1];
            float bx = coords[i];
            float by = coords[i + 1];
            float cx = coords[i + 2];
            float cy = coords[i + 3];
            float cross = ((bx - ax) * (cy - ay)) - ((by - ay) * (cx - ax));
            if (MathF.Abs(cross) > 0.01f)
            {
                simplified.Add(bx);
                simplified.Add(by);
            }
        }

        simplified.Add(coords[^2]);
        simplified.Add(coords[^1]);

        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;
        for (int i = 0; i < simplified.Count; i += 2)
        {
            float x = simplified[i];
            float y = simplified[i + 1];
            minX = MathF.Min(minX, x);
            minY = MathF.Min(minY, y);
            maxX = MathF.Max(maxX, x);
            maxY = MathF.Max(maxY, y);
        }

        outLinks.Add(new RoadLink([.. simplified], level, minX, minY, maxX, maxY, isBridge));
    }

    private void EnsureCell(int idx, int width, int gx0, int gy0, byte[] kind, byte[] elevLevel)
    {
        if (kind[idx] != 0)
        {
            return;
        }

        int cx = gx0 + (idx % width);
        int cy = gy0 + (idx / width);
        int wx = (cx * CoarseStep) + (CoarseStep / 2);
        int wy = (cy * CoarseStep) + (CoarseStep / 2);

        MapTile t = _tiles.SampleTile(wx, wy);
        elevLevel[idx] = t.HeightLevel;

        if (t.Biome is BiomeType.DeepOcean or BiomeType.ShallowWater)
        {
            // Inland river or lake channels can be bridged, however deep; so can wading-depth water.
            if (_rivers is not null && _rivers.GetDistance(wx, wy) <= WorldConstants.RiverChannel + 1f)
            {
                kind[idx] = 2; // Bridgeable river water
            }
            else if (WaterDepth.TierOf(t) <= WaterDepthTier.Shallow)
            {
                kind[idx] = 2;
            }
            else
            {
                kind[idx] = 3; // Impassable sea
            }

            return;
        }

        kind[idx] = 1;
    }

    private (int X, int Y) ResolveGateTile(in PointOfInterest poi, int towardX, int towardY)
    {
        if (poi.Type == PointOfInterestType.MineEntrance)
        {
            return (poi.TileX, poi.TileY + 2);
        }

        if (poi.Radius < 9)
        {
            return (poi.TileX, poi.TileY);
        }

        float dx = towardX - poi.TileX;
        float dy = towardY - poi.TileY;
        float len = MathF.Sqrt((dx * dx) + (dy * dy));
        if (len < 1f)
        {
            return (poi.TileX, poi.TileY);
        }

        float gateDist = poi.Radius * 0.72f;
        int gx = (int)MathF.Round(poi.TileX + (dx / len * gateDist));
        int gy = (int)MathF.Round(poi.TileY + (dy / len * gateDist));
        MapTile gateTile = _tiles.SampleTile(gx, gy);
        return !gateTile.IsSolid && gateTile.Biome is not (BiomeType.DeepOcean or BiomeType.ShallowWater)
            ? (gx, gy)
            : (poi.TileX, poi.TileY);
    }

    private static float Heuristic(int x0, int y0, int x1, int y1) =>
        (Math.Abs(x1 - x0) + Math.Abs(y1 - y0)) * CoarseStep;

    private static void TrimQueue<TValue>(ConcurrentDictionary<long, TValue> dict, ConcurrentQueue<long> queue, int max)
    {
        int guard = 0;
        while (dict.Count >= max && guard < 8 && queue.TryDequeue(out long oldest))
        {
            guard++;
            _ = dict.TryRemove(oldest, out _);
        }
    }

    private static long PackTile(int x, int y) => ((long)x << 32) | (uint)y;

    private static int FloorDiv(int a, int b) => a >= 0 ? a / b : ((a + 1) / b) - 1;
}
