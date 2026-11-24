// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Carbon.Maps.Random;

namespace Carbon.Maps.Hydrology;

/// <summary>
/// Physically ordered river network for the infinite world.
/// </summary>
/// <remarks>
/// <para>
/// Rivers obey the terrain: each one springs from a source in the highlands and walks down the steepest
/// descent of the smooth relief (D8 on an 8-tile grid) until it reaches the sea. Two rivers that reach the
/// same cell follow the same downhill path from there on, so they merge into one, and the channel widens
/// with the number of rivers it carries. A river trapped in a depression may breach a shallow rim; if it
/// cannot, it ends in a lake.
/// </para>
/// <para>
/// The world is processed in regions of <see cref="RegionSize"/> tiles. A river is at most
/// <see cref="MaxRiverLength"/> tiles long, so every river that can touch a region springs within that
/// distance of it. Each region traces all those sources, which makes the result identical whichever region
/// computes it: chunks agree across region borders and every machine produces the same rivers.
/// </para>
/// </remarks>
internal sealed class RiverNetwork
{
    /// <summary>Region edge in tiles.</summary>
    public const int RegionSize = 512;

    /// <summary>Flow grid cell edge in tiles.</summary>
    public const int Cell = 8;

    /// <summary>Maximum river length in tiles.</summary>
    public const int MaxRiverLength = 1024;

    /// <summary>One river source candidate per square of this many tiles.</summary>
    public const int SourceSpacing = 88;

    /// <summary>Relief below which a cell is sea: the river mouth.</summary>
    public const float SeaRelief = 0.32f;

    /// <summary>Relief band in which springs appear.</summary>
    public const float MinSourceRelief = 0.62f;

    /// <summary>Upper relief bound for springs (bare peaks above it stay dry).</summary>
    public const float MaxSourceRelief = 0.86f;

    // Stored distances are quantised to 1/16 tile; FarAway means "no river within range".
    private const float Quantum = 16f;
    private const byte FarAway = 255;
    private const float MaxStoredDistance = (FarAway - 1) / Quantum;

    private const int MaxCachedRegions = 48;

    // Extra grid border (tiles) used only by depression filling.
    private const int FillApron = 384;

    // Elevation samples are cached in blocks of BlockCells x BlockCells cells and shared between regions,
    // whose grids overlap by more than 80 %.
    private const int BlockCells = 64;
    private const int MaxCachedBlocks = 400;
    // Priority-flood epsilon: filled depressions keep a tiny downhill gradient towards their outlet.
    private const float FillEpsilon = 1e-5f;

    // A cell whose filled water level is this far above its ground is lake bed.
    private const float LakeDepth = 0.008f;

    // Preference for stepping into a cell that already carries a river, so neighbouring streams merge
    // into one network instead of running in parallel down an even slope.
    private const float JoinBonus = 0.0035f;

    private static readonly (int Dx, int Dy, float Len)[] s_d8 =
    [
        (1, 0, 1f), (-1, 0, 1f), (0, 1, 1f), (0, -1, 1f),
        (1, 1, 1.4142135f), (1, -1, 1.4142135f), (-1, 1, 1.4142135f), (-1, -1, 1.4142135f),
    ];

    private readonly ulong _seed;
    private readonly Func<int, int, float> _relief;
    private readonly ConcurrentDictionary<long, Lazy<byte[]>> _regions = new();
    private readonly ConcurrentDictionary<long, Lazy<float[]>> _blocks = new();

    /// <summary>Initializes a new instance of the <see cref="RiverNetwork"/> class.</summary>
    /// <param name="seed">World seed.</param>
    /// <param name="relief">Terrain elevation used for routing at a world tile; must be deterministic.</param>
    public RiverNetwork(ulong seed, Func<int, int, float> relief)
    {
        _seed = seed;
        _relief = relief;
    }

    /// <summary>
    /// Distance from a tile to the nearest river or lake, divided by the local channel half-width scale.
    /// Values ≤ 2.2 are water. Returns <see cref="float.MaxValue"/> beyond about 15 units.
    /// </summary>
    /// <param name="worldX">World tile X.</param>
    /// <param name="worldY">World tile Y.</param>
    /// <returns>Width-scaled distance.</returns>
    public float GetDistance(int worldX, int worldY)
    {
        int rx = FloorDiv(worldX, RegionSize);
        int ry = FloorDiv(worldY, RegionSize);
        byte[] field = this.GetRegion(rx, ry);
        byte q = field[((worldY - (ry * RegionSize)) * RegionSize) + (worldX - (rx * RegionSize))];
        return q == FarAway ? float.MaxValue : q / Quantum;
    }

    /// <summary>
    /// Starts building, on the thread pool, every region within <paramref name="radiusTiles"/> of a point,
    /// so the region is ready before the player's chunks need it. Already built or building regions are skipped.
    /// </summary>
    /// <param name="worldX">World tile X.</param>
    /// <param name="worldY">World tile Y.</param>
    /// <param name="radiusTiles">Radius in tiles.</param>
    public void Prewarm(int worldX, int worldY, int radiusTiles)
    {
        int rx0 = FloorDiv(worldX - radiusTiles, RegionSize);
        int rx1 = FloorDiv(worldX + radiusTiles, RegionSize);
        int ry0 = FloorDiv(worldY - radiusTiles, RegionSize);
        int ry1 = FloorDiv(worldY + radiusTiles, RegionSize);
        for (int ry = ry0; ry <= ry1; ry++)
        {
            for (int rx = rx0; rx <= rx1; rx++)
            {
                long key = ((long)rx << 32) | (uint)ry;
                if (_regions.ContainsKey(key))
                {
                    continue;
                }

                int bx = rx;
                int by = ry;
                _ = Task.Run(() => this.GetRegion(bx, by));
            }
        }
    }

    private float[] SampleGrid(int gx0, int gy0, int g)
    {
        float[] grid = new float[g * g];
        int bx0 = FloorDiv(gx0, BlockCells);
        int by0 = FloorDiv(gy0, BlockCells);
        int bx1 = FloorDiv(gx0 + g - 1, BlockCells);
        int by1 = FloorDiv(gy0 + g - 1, BlockCells);
        for (int by = by0; by <= by1; by++)
        {
            for (int bx = bx0; bx <= bx1; bx++)
            {
                float[] block = this.GetBlock(bx, by);
                int cx0 = Math.Max(bx * BlockCells, gx0);
                int cx1 = Math.Min((bx + 1) * BlockCells, gx0 + g);
                int cy0 = Math.Max(by * BlockCells, gy0);
                int cy1 = Math.Min((by + 1) * BlockCells, gy0 + g);
                for (int cy = cy0; cy < cy1; cy++)
                {
                    int src = ((cy - (by * BlockCells)) * BlockCells) + (cx0 - (bx * BlockCells));
                    int dst = ((cy - gy0) * g) + (cx0 - gx0);
                    Array.Copy(block, src, grid, dst, cx1 - cx0);
                }
            }
        }

        return grid;
    }

    private float[] GetBlock(int bx, int by)
    {
        long key = ((long)bx << 32) | (uint)by;
        Evict(_blocks, key, MaxCachedBlocks);
        return _blocks.GetOrAdd(key, _ => new Lazy<float[]>(() =>
        {
            float[] block = new float[BlockCells * BlockCells];
            for (int cy = 0; cy < BlockCells; cy++)
            {
                for (int cx = 0; cx < BlockCells; cx++)
                {
                    int wx = (((bx * BlockCells) + cx) * Cell) + (Cell / 2);
                    int wy = (((by * BlockCells) + cy) * Cell) + (Cell / 2);
                    block[(cy * BlockCells) + cx] = _relief(wx, wy);
                }
            }

            return block;
        })).Value;
    }

    // Approximate eviction: drop an arbitrary entry; it is recomputed identically if needed again.
    private static void Evict<T>(ConcurrentDictionary<long, T> cache, long keep, int max)
    {
        if (cache.Count <= max)
        {
            return;
        }

        foreach (long k in cache.Keys)
        {
            if (k != keep && cache.TryRemove(k, out _))
            {
                return;
            }
        }
    }

    private byte[] GetRegion(int rx, int ry)
    {
        long key = ((long)rx << 32) | (uint)ry;
        Evict(_regions, key, MaxCachedRegions);

        return _regions.GetOrAdd(key, k => new Lazy<byte[]>(() => this.BuildRegion(rx, ry))).Value;
    }

    private byte[] BuildRegion(int rx, int ry)
    {
        int x0 = rx * RegionSize;
        int y0 = ry * RegionSize;

        // Flow grid covering the region, the maximum river length on every side (every river that can reach
        // the region springs inside it) and an extra fill apron, so the artificial outlets on the grid edge
        // are too far away to change how basins near the region fill (keeps rivers continuous across
        // region borders).
        int margin = MaxRiverLength + FillApron;
        int gx0 = FloorDiv(x0 - margin, Cell);
        int gy0 = FloorDiv(y0 - margin, Cell);
        int g = ((RegionSize + (2 * margin)) / Cell) + 1;
        float[] relief = this.SampleGrid(gx0, gy0, g);

        // 1. Fill depressions (priority-flood from the grid edge and the sea). Water in a basin rises to the
        //    lowest point of its rim, which makes the filled surface drain everywhere; the filled part of
        //    a basin is its lake.
        float[] filled = Fill(relief, g);

        // 2. Trace every source whose river could reach this region.
        List<(float Relief, int Cx, int Cy)> sources = new();
        List<int> lakeEnds = new();
        int[] flow = new int[g * g];
        int[] next = new int[g * g];
        Array.Fill(next, -1);
        int s0x = FloorDiv(x0 - MaxRiverLength, SourceSpacing);
        int s0y = FloorDiv(y0 - MaxRiverLength, SourceSpacing);
        int s1x = FloorDiv(x0 + RegionSize + MaxRiverLength, SourceSpacing);
        int s1y = FloorDiv(y0 + RegionSize + MaxRiverLength, SourceSpacing);
        for (int sy = s0y; sy <= s1y; sy++)
        {
            for (int sx = s0x; sx <= s1x; sx++)
            {
                ulong h = SpatialHash.Hash(_seed ^ 0x5D1C3A7B9E2F4061UL, sx, sy);
                if ((h & 0xFF) >= 110)
                {
                    continue; // ~43 % of candidates become springs
                }

                int px = (sx * SourceSpacing) + (int)((h >> 8) % SourceSpacing);
                int py = (sy * SourceSpacing) + (int)((h >> 24) % SourceSpacing);
                int cx = FloorDiv(px, Cell) - gx0;
                int cy = FloorDiv(py, Cell) - gy0;
                if (cx < 1 || cy < 1 || cx >= g - 1 || cy >= g - 1)
                {
                    continue;
                }

                float r = relief[(cy * g) + cx];
                if (r is < MinSourceRelief or > MaxSourceRelief)
                {
                    continue;
                }

                sources.Add((r, cx, cy));
            }
        }

        // Highest springs first, so lower ones can join the rivers already flowing past them.
        sources.Sort((a, b) => b.Relief != a.Relief ? b.Relief.CompareTo(a.Relief) : (a.Cy != b.Cy ? a.Cy.CompareTo(b.Cy) : a.Cx.CompareTo(b.Cx)));
        HashSet<int> sourceCells = new();
        foreach ((float _, int cx, int cy) in sources)
        {
            int start = (cy * g) + cx;
            if (flow[start] > 0 || !sourceCells.Add(start))
            {
                continue; // already on a river
            }

            List<int> path = Trace(filled, flow, g, cx, cy, out bool endsInLake);
            if (path.Count < 3)
            {
                continue;
            }

            for (int i = 0; i + 1 < path.Count; i++)
            {
                next[path[i]] = path[i + 1];
            }

            if (endsInLake)
            {
                lakeEnds.Add(path[^1]);
            }

            // Add this river's water to its own cells and to every cell downstream of the junction.
            int guard = 0;
            for (int c = start; c >= 0 && guard < 4 * g; c = next[c], guard++)
            {
                flow[c]++;
            }
        }

        // 3. Rasterise channels into the region's distance field.
        byte[] field = new byte[RegionSize * RegionSize];
        Array.Fill(field, FarAway);
        // Main upstream stem of every cell (the inflow carrying the most water), used as the previous
        // control point so channels are drawn as smooth Catmull-Rom curves instead of 8-tile straight segments.
        int[] prev = new int[g * g];
        Array.Fill(prev, -1);
        for (int c = 0; c < next.Length; c++)
        {
            int n = next[c];
            if (n >= 0 && (prev[n] < 0 || flow[c] > flow[prev[n]] || (flow[c] == flow[prev[n]] && c < prev[n])))
            {
                prev[n] = c;
            }
        }

        const int Steps = 4;
        for (int a = 0; a < next.Length; a++)
        {
            int b = next[a];
            if (b < 0)
            {
                continue;
            }

            int p0 = prev[a] >= 0 ? prev[a] : a;
            int p3 = next[b] >= 0 ? next[b] : b;
            (float x0p, float y0p) = this.CellPoint(p0, g, gx0, gy0);
            (float ax, float ay) = this.CellPoint(a, g, gx0, gy0);
            (float bx, float by) = this.CellPoint(b, g, gx0, gy0);
            (float x3p, float y3p) = this.CellPoint(p3, g, gx0, gy0);
            float wa = Width(flow[a]);
            float wb = Width(flow[b]);

            float sx = ax;
            float sy = ay;
            for (int i = 1; i <= Steps; i++)
            {
                float t = i / (float)Steps;
                float ex = CatmullRom(x0p, ax, bx, x3p, t);
                float ey = CatmullRom(y0p, ay, by, y3p, t);
                StampSegment(field, x0, y0, sx, sy, ex, ey, wa + ((wb - wa) * (i - 1) / Steps), wa + ((wb - wa) * i / Steps));
                sx = ex;
                sy = ey;
            }
        }

        // 4. Lakes: every basin a river flows through is filled up to its outlet.
        bool[] isLake = new bool[g * g];
        Stack<int> pending = new();
        for (int c = 0; c < flow.Length; c++)
        {
            if (flow[c] == 0 || isLake[c] || filled[c] - relief[c] < LakeDepth)
            {
                continue;
            }

            isLake[c] = true;
            pending.Push(c);
            while (pending.Count > 0)
            {
                int k = pending.Pop();
                int kx = k % g;
                int ky = k / g;
                for (int i = 0; i < 4; i++)
                {
                    int nx = kx + s_d8[i].Dx;
                    int ny = ky + s_d8[i].Dy;
                    int n = (ny * g) + nx;
                    if (nx >= 0 && ny >= 0 && nx < g && ny < g && !isLake[n] && filled[n] - relief[n] >= LakeDepth)
                    {
                        isLake[n] = true;
                        pending.Push(n);
                    }
                }
            }
        }

        StampLakeShores(field, x0, y0, gx0, gy0, g, filled, relief, isLake);

        foreach (int end in lakeEnds)
        {
            (float lx, float ly) = this.CellPoint(end, g, gx0, gy0);
            StampLake(field, x0, y0, lx, ly, MathF.Min(2.5f + (1.4f * MathF.Sqrt(flow[end])), 14f));
        }

        return field;
    }

    // Priority-flood (Barnes et al. 2014, with epsilon): every cell's water level becomes the lowest level
    // from which water can still drain to the sea or out of the grid.
    private static float[] Fill(float[] relief, int g)
    {
        float[] filled = new float[relief.Length];
        bool[] done = new bool[relief.Length];
        PriorityQueue<int, float> open = new();
        for (int c = 0; c < relief.Length; c++)
        {
            int cx = c % g;
            int cy = c / g;
            if (cx == 0 || cy == 0 || cx == g - 1 || cy == g - 1 || relief[c] < SeaRelief)
            {
                filled[c] = relief[c];
                done[c] = true;
                open.Enqueue(c, relief[c]);
            }
        }

        while (open.TryDequeue(out int c, out float level))
        {
            int cx = c % g;
            int cy = c / g;
            for (int i = 0; i < s_d8.Length; i++)
            {
                int nx = cx + s_d8[i].Dx;
                int ny = cy + s_d8[i].Dy;
                if (nx < 0 || ny < 0 || nx >= g || ny >= g)
                {
                    continue;
                }

                int n = (ny * g) + nx;
                if (done[n])
                {
                    continue;
                }

                done[n] = true;
                filled[n] = MathF.Max(relief[n], level + FillEpsilon);
                open.Enqueue(n, filled[n]);
            }
        }

        return filled;
    }

    // Steepest descent on the filled surface (so it always reaches an outlet), preferring to join rivers
    // already traced. Ends at the sea, at the grid edge, on joining a river, or at the length cap.
    private static List<int> Trace(float[] filled, int[] flow, int g, int cx, int cy, out bool endsInLake)
    {
        List<int> path = new() { (cy * g) + cx };
        endsInLake = false;
        int maxSteps = MaxRiverLength / Cell;

        for (int step = 0; step < maxSteps; step++)
        {
            float here = filled[(cy * g) + cx];
            if (here < SeaRelief)
            {
                return path;
            }

            int bestX = -1;
            int bestY = -1;
            float bestScore = 0f;
            for (int i = 0; i < s_d8.Length; i++)
            {
                int nx = cx + s_d8[i].Dx;
                int ny = cy + s_d8[i].Dy;
                if (nx < 0 || ny < 0 || nx >= g || ny >= g)
                {
                    continue;
                }

                int ni = (ny * g) + nx;
                float nr = filled[ni];
                if (nr >= here)
                {
                    continue; // strictly downhill only: no loops, no uphill flow
                }

                float score = ((here - nr) / s_d8[i].Len) + (flow[ni] > 0 ? JoinBonus : 0f);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestX = nx;
                    bestY = ny;
                }
            }

            if (bestX < 0)
            {
                return path; // grid edge outlet
            }

            cx = bestX;
            cy = bestY;
            int idx = (cy * g) + cx;
            path.Add(idx);

            // Joined an existing river: from here on its course is already traced.
            if (flow[idx] > 0)
            {
                return path;
            }
        }

        // Length cap reached inland: the river ends in a terminal (endorheic) lake rather than stopping dry.
        endsInLake = true;
        return path;
    }

    // Cell centre jittered by a hash of the cell, so merged rivers share their points and the
    // D8 staircase turns into a meandering line.
    private (float X, float Y) CellPoint(int index, int g, int gx0, int gy0)
    {
        int cx = gx0 + (index % g);
        int cy = gy0 + (index / g);
        ulong h = SpatialHash.Hash(_seed ^ 0x2B7E151628AED2A6UL, cx, cy);
        float jx = (((h & 0xFFFF) / 65535f) - 0.5f) * (Cell * 0.45f);
        float jy = ((((h >> 16) & 0xFFFF) / 65535f) - 0.5f) * (Cell * 0.45f);
        return ((cx * Cell) + (Cell * 0.5f) + jx, (cy * Cell) + (Cell * 0.5f) + jy);
    }

    private static float CatmullRom(float p0, float p1, float p2, float p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        return 0.5f * ((2f * p1) + ((p2 - p0) * t) + (((2f * p0) - (5f * p1) + (4f * p2) - p3) * t2) + (((3f * p1) - p0 - (3f * p2) + p3) * t3));
    }

    // Channel half-width scale: a spring is a narrow stream, every merged river widens it.
    private static float Width(int flow) => MathF.Min(0.85f + (0.42f * MathF.Log2(MathF.Max(flow, 1))), 2.6f);

    private static void StampSegment(byte[] field, int x0, int y0, float ax, float ay, float bx, float by, float wa, float wb)
    {
        float reach = MaxStoredDistance * MathF.Max(wa, wb);
        int minX = Math.Max((int)MathF.Floor(MathF.Min(ax, bx) - reach) - x0, 0);
        int maxX = Math.Min((int)MathF.Ceiling(MathF.Max(ax, bx) + reach) - x0, RegionSize - 1);
        int minY = Math.Max((int)MathF.Floor(MathF.Min(ay, by) - reach) - y0, 0);
        int maxY = Math.Min((int)MathF.Ceiling(MathF.Max(ay, by) + reach) - y0, RegionSize - 1);
        if (minX > maxX || minY > maxY)
        {
            return;
        }

        float dx = bx - ax;
        float dy = by - ay;
        float lenSq = MathF.Max((dx * dx) + (dy * dy), 1e-6f);
        for (int ty = minY; ty <= maxY; ty++)
        {
            float py = y0 + ty + 0.5f;
            for (int tx = minX; tx <= maxX; tx++)
            {
                float px = x0 + tx + 0.5f;
                float t = Math.Clamp((((px - ax) * dx) + ((py - ay) * dy)) / lenSq, 0f, 1f);
                float ex = px - (ax + (t * dx));
                float ey = py - (ay + (t * dy));
                float d = MathF.Sqrt((ex * ex) + (ey * ey)) / (wa + ((wb - wa) * t));
                Store(field, (ty * RegionSize) + tx, d);
            }
        }
    }

    private static void StampLake(byte[] field, int x0, int y0, float lx, float ly, float radius)
    {
        float reach = radius + MaxStoredDistance;
        int minX = Math.Max((int)MathF.Floor(lx - reach) - x0, 0);
        int maxX = Math.Min((int)MathF.Ceiling(lx + reach) - x0, RegionSize - 1);
        int minY = Math.Max((int)MathF.Floor(ly - reach) - y0, 0);
        int maxY = Math.Min((int)MathF.Ceiling(ly + reach) - y0, RegionSize - 1);
        for (int ty = minY; ty <= maxY; ty++)
        {
            for (int tx = minX; tx <= maxX; tx++)
            {
                float ex = x0 + tx + 0.5f - lx;
                float ey = y0 + ty + 0.5f - ly;
                float d = MathF.Max(MathF.Sqrt((ex * ex) + (ey * ey)) - radius, 0f);
                Store(field, (ty * RegionSize) + tx, d);
            }
        }
    }

    // Lake shorelines follow the contour of the water depth (filled level minus ground), smoothed and
    // interpolated between cell centres, instead of a union of per-cell discs. Outside the lake the stored
    // value grows with the distance to that contour, so beaches and riparian zones stay smooth too.
    private static void StampLakeShores(byte[] field, int x0, int y0, int gx0, int gy0, int g, float[] filled, float[] relief, bool[] isLake)
    {
        const float Threshold = LakeDepth * 0.5f;

        // Depth of lake cells only (other basins hold no river and stay dry), with a 3x3 blur so the contour
        // is rounded rather than following the cell grid.
        float[] depth = new float[g * g];
        int minCx = g;
        int minCy = g;
        int maxCx = -1;
        int maxCy = -1;
        for (int c = 0; c < depth.Length; c++)
        {
            if (!isLake[c])
            {
                continue;
            }

            depth[c] = filled[c] - relief[c];
            minCx = Math.Min(minCx, c % g);
            maxCx = Math.Max(maxCx, c % g);
            minCy = Math.Min(minCy, c / g);
            maxCy = Math.Max(maxCy, c / g);
        }

        if (maxCx < 0)
        {
            return;
        }

        float[] smooth = new float[g * g];
        for (int cy = Math.Max(minCy - 2, 1); cy <= Math.Min(maxCy + 2, g - 2); cy++)
        {
            for (int cx = Math.Max(minCx - 2, 1); cx <= Math.Min(maxCx + 2, g - 2); cx++)
            {
                float sum = 0f;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        float w = (dx == 0 ? 2f : 1f) * (dy == 0 ? 2f : 1f);
                        sum += depth[((cy + dy) * g) + cx + dx] * w;
                    }
                }

                smooth[(cy * g) + cx] = sum / 16f;
            }
        }

        int tx0 = Math.Max(((gx0 + minCx - 3) * Cell) - x0, 0);
        int tx1 = Math.Min(((gx0 + maxCx + 4) * Cell) - x0, RegionSize - 1);
        int ty0 = Math.Max(((gy0 + minCy - 3) * Cell) - y0, 0);
        int ty1 = Math.Min(((gy0 + maxCy + 4) * Cell) - y0, RegionSize - 1);
        for (int ty = ty0; ty <= ty1; ty++)
        {
            float v = ((y0 + ty + 0.5f - (Cell * 0.5f)) / Cell) - gy0;
            int cy = (int)MathF.Floor(v);
            if (cy < 0 || cy >= g - 1)
            {
                continue;
            }

            float fy = v - cy;
            for (int tx = tx0; tx <= tx1; tx++)
            {
                float u = ((x0 + tx + 0.5f - (Cell * 0.5f)) / Cell) - gx0;
                int cx = (int)MathF.Floor(u);
                if (cx < 0 || cx >= g - 1)
                {
                    continue;
                }

                float fx = u - cx;
                float d00 = smooth[(cy * g) + cx];
                float d10 = smooth[(cy * g) + cx + 1];
                float d01 = smooth[((cy + 1) * g) + cx];
                float d11 = smooth[((cy + 1) * g) + cx + 1];
                if (d00 <= 0f && d10 <= 0f && d01 <= 0f && d11 <= 0f)
                {
                    continue;
                }

                float top = d00 + ((d10 - d00) * fx);
                float bottom = d01 + ((d11 - d01) * fx);
                float d = top + ((bottom - top) * fy);
                if (d >= Threshold)
                {
                    Store(field, (ty * RegionSize) + tx, 0f);
                    continue;
                }

                // Gradient of the bilinear surface (per tile) turns the depth deficit into a distance.
                float gx = (d10 - d00 + ((d11 - d01 - (d10 - d00)) * fy)) / Cell;
                float gy = (bottom - top) / Cell;
                float grad = MathF.Max(MathF.Sqrt((gx * gx) + (gy * gy)), 1e-6f);
                Store(field, (ty * RegionSize) + tx, 2.25f + ((Threshold - d) / grad));
            }
        }
    }

    private static void Store(byte[] field, int index, float distance)
    {
        if (distance >= MaxStoredDistance)
        {
            return;
        }

        byte q = (byte)(distance * Quantum);
        if (q < field[index])
        {
            field[index] = q;
        }
    }

    private static int FloorDiv(int a, int b) => a >= 0 ? a / b : ((a + 1) / b) - 1;
}
