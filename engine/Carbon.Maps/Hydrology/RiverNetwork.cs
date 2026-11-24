// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

    private byte[] GetRegion(int rx, int ry)
    {
        long key = ((long)rx << 32) | (uint)ry;
        if (_regions.Count > MaxCachedRegions)
        {
            // Approximate eviction: drop an arbitrary region; it is recomputed identically if needed again.
            foreach (long k in _regions.Keys)
            {
                if (k != key && _regions.TryRemove(k, out _))
                {
                    break;
                }
            }
        }

        return _regions.GetOrAdd(key, k => new Lazy<byte[]>(() => this.BuildRegion(rx, ry))).Value;
    }

    private byte[] BuildRegion(int rx, int ry)
    {
        int x0 = rx * RegionSize;
        int y0 = ry * RegionSize;

        // Flow grid covering the region plus the maximum river length on every side.
        int gx0 = FloorDiv(x0 - MaxRiverLength, Cell);
        int gy0 = FloorDiv(y0 - MaxRiverLength, Cell);
        int g = ((RegionSize + (2 * MaxRiverLength)) / Cell) + 1;
        float[] relief = new float[g * g];
        for (int cy = 0; cy < g; cy++)
        {
            for (int cx = 0; cx < g; cx++)
            {
                relief[(cy * g) + cx] = _relief(((gx0 + cx) * Cell) + (Cell / 2), ((gy0 + cy) * Cell) + (Cell / 2));
            }
        }

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
        for (int a = 0; a < next.Length; a++)
        {
            int b = next[a];
            if (b >= 0)
            {
                (float ax, float ay) = this.CellPoint(a, g, gx0, gy0);
                (float bx, float by) = this.CellPoint(b, g, gx0, gy0);
                float wa = Width(flow[a]);
                float wb = Width(flow[b]);
                StampSegment(field, x0, y0, ax, ay, bx, by, wa, wb);
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
                (float lx, float ly) = LakeCellCentre(k, g, gx0, gy0);
                StampLake(field, x0, y0, lx, ly, Cell * 0.8f);
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

    private static (float X, float Y) LakeCellCentre(int index, int g, int gx0, int gy0) =>
        (((gx0 + (index % g)) * Cell) + (Cell * 0.5f), ((gy0 + (index / g)) * Cell) + (Cell * 0.5f));

    // Cell centre jittered by a hash of the cell, so merged rivers share their points and the
    // D8 staircase turns into a meandering line.
    private (float X, float Y) CellPoint(int index, int g, int gx0, int gy0)
    {
        int cx = gx0 + (index % g);
        int cy = gy0 + (index / g);
        ulong h = SpatialHash.Hash(_seed ^ 0x2B7E151628AED2A6UL, cx, cy);
        float jx = (((h & 0xFFFF) / 65535f) - 0.5f) * (Cell * 0.7f);
        float jy = ((((h >> 16) & 0xFFFF) / 65535f) - 0.5f) * (Cell * 0.7f);
        return ((cx * Cell) + (Cell * 0.5f) + jx, (cy * Cell) + (Cell * 0.5f) + jy);
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
