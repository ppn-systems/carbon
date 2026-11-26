// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Maps.Noise;
using Carbon.Primitives.Hashing;

namespace Carbon.Maps.Hydrology;

/// <summary>
/// High-throughput, zero-LOH deterministic river, estuary, and lake network for an infinite world.
/// Uses coarse D8 flow routing over priority-flooded relief backed by shared pooled buffers.
/// </summary>
internal sealed class RiverNetwork : IRiverDistanceSource
{
    /// <summary>Region edge in tiles.</summary>
    public const int RegionSize = 512;

    /// <summary>Flow grid cell edge in tiles.</summary>
    public const int Cell = 16;

    /// <summary>Maximum river length in tiles.</summary>
    public const int MaxRiverLength = 896;

    /// <summary>One river source candidate per square of this many tiles.</summary>
    public const int SourceSpacing = 88;

    /// <summary>Relief below which a cell is sea: the river mouth.</summary>
    public const float SeaRelief = WorldConstants.SeaLevel;

    /// <summary>Relief band in which springs appear.</summary>
    public const float MinSourceRelief = 0.58f;

    /// <summary>Upper relief bound for springs.</summary>
    public const float MaxSourceRelief = 0.88f;

    private const float Quantum = 16f;
    private const byte FarAway = 255;
    private const float MaxStoredDistance = (FarAway - 1) / Quantum;

    private const int MaxCachedRegions = 128;
    private const int MaxCachedBlocks = 640;

    private const int EstuaryCells = 3;
    private const float MaxChannelScale = 5f;
    private const int FillApron = 384;
    private const int BlockCells = 64;
    private const float FillEpsilon = 1e-5f;
    private const float LakeDepth = 0.008f;
    private const float MaxLakeDepth = 0.045f;

    // Any cell the priority flood raised (by at least one FillEpsilon) is under the basin's spill level.
    private const float FloodedDepth = FillEpsilon * 0.5f;

    // How many cells of a lake basin's shallow rim join the lake.
    private const int LakeRimCells = 2;

    // Lake shores are sampled through a noise warp of up to this many cells, at this frequency (per tile).
    private const float ShoreWarpCells = 0.45f;
    private const float ShoreWarpFrequency = 1f / 37f;
    private const int MaxLakeRadiusCells = 24;

    private readonly record struct RegionData(byte[] DistanceField, byte[] DeepMask);

    private readonly record struct CacheKey(ulong Seed, int FreqBits, int X, int Y);

    private static readonly (int Dx, int Dy, float Len)[] s_d8 =
    [
        (1, 0, 1f), (-1, 0, 1f), (0, 1, 1f), (0, -1, 1f),
        (1, 1, 1.4142135f), (1, -1, 1.4142135f), (-1, 1, 1.4142135f), (-1, -1, 1.4142135f),
    ];

    private static readonly ConcurrentDictionary<CacheKey, Lazy<RegionData>> s_sharedRegions = new();
    private static readonly ConcurrentQueue<CacheKey> s_regionOrder = new();
    private static readonly ConcurrentDictionary<CacheKey, Lazy<float[]>> s_sharedBlocks = new();
    private static readonly ConcurrentQueue<CacheKey> s_blockOrder = new();

    private readonly ulong _seed;
    private readonly int _frequencyBits;
    private readonly IRoutingTerrain _relief;
    private readonly SimplexNoise2D _shoreWarp;

    /// <summary>Initializes a new instance of the <see cref="RiverNetwork"/> class.</summary>
    /// <param name="seed">World seed.</param>
    /// <param name="frequency">Macro spatial frequency.</param>
    /// <param name="relief">Terrain elevation used for routing at a world tile; must be deterministic.</param>
    public RiverNetwork(ulong seed, float frequency, IRoutingTerrain relief)
    {
        _seed = seed;
        _frequencyBits = BitConverter.SingleToInt32Bits(frequency);
        _relief = relief ?? throw new ArgumentNullException(nameof(relief));
        _shoreWarp = new SimplexNoise2D(seed ^ 0x6C8E9CF570932BD5UL);
    }

    /// <inheritdoc/>
    public float GetDistance(int worldX, int worldY) =>
        this.GetDistance(worldX, worldY, out _);

    /// <inheritdoc/>
    public float GetDistance(int worldX, int worldY, out bool isDeepChannel)
    {
        int rx = FloorDiv(worldX, RegionSize);
        int ry = FloorDiv(worldY, RegionSize);
        RegionData data = this.GetRegion(rx, ry);
        int index = ((worldY - (ry * RegionSize)) * RegionSize) + (worldX - (rx * RegionSize));
        byte q = data.DistanceField[index];
        isDeepChannel = q < FarAway && (data.DeepMask[index >> 3] & (1 << (index & 7))) != 0;
        return q == FarAway ? float.MaxValue : q / Quantum;
    }

    /// <summary>
    /// Starts building every region within <paramref name="radiusTiles"/> on the thread pool if not already cached.
    /// </summary>
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
                CacheKey key = new(_seed, _frequencyBits, rx, ry);
                if (s_sharedRegions.ContainsKey(key))
                {
                    continue;
                }

                int bx = rx;
                int by = ry;
                Lazy<RegionData> lazy = this.GetOrRegisterRegionLazy(key, bx, by);
                if (!lazy.IsValueCreated)
                {
                    _ = Task.Run(() => _ = lazy.Value);
                }
            }
        }
    }

    private RegionData GetRegion(int rx, int ry)
    {
        CacheKey key = new(_seed, _frequencyBits, rx, ry);
        if (s_sharedRegions.TryGetValue(key, out Lazy<RegionData>? existing))
        {
            return existing.Value;
        }

        return this.GetOrRegisterRegionLazy(key, rx, ry).Value;
    }

    private Lazy<RegionData> GetOrRegisterRegionLazy(CacheKey key, int rx, int ry)
    {
        TrimCache(s_sharedRegions, s_regionOrder, key, MaxCachedRegions);
        Lazy<RegionData> created = new(() => this.BuildRegion(rx, ry), LazyThreadSafetyMode.ExecutionAndPublication);
        Lazy<RegionData> active = s_sharedRegions.GetOrAdd(key, created);
        if (ReferenceEquals(active, created))
        {
            s_regionOrder.Enqueue(key);
        }

        return active;
    }

    private float[] GetBlock(int bx, int by)
    {
        CacheKey key = new(_seed, _frequencyBits, bx, by);
        if (s_sharedBlocks.TryGetValue(key, out Lazy<float[]>? existing))
        {
            return existing.Value;
        }

        TrimCache(s_sharedBlocks, s_blockOrder, key, MaxCachedBlocks);
        Lazy<float[]> created = new(() => this.BuildBlock(bx, by), LazyThreadSafetyMode.ExecutionAndPublication);
        Lazy<float[]> active = s_sharedBlocks.GetOrAdd(key, created);
        if (ReferenceEquals(active, created))
        {
            s_blockOrder.Enqueue(key);
        }

        return active.Value;
    }

    private float[] BuildBlock(int bx, int by)
    {
        float[] block = new float[BlockCells * BlockCells];
        for (int cy = 0; cy < BlockCells; cy++)
        {
            int wy = (((by * BlockCells) + cy) * Cell) + (Cell / 2);
            int row = cy * BlockCells;
            for (int cx = 0; cx < BlockCells; cx++)
            {
                int wx = (((bx * BlockCells) + cx) * Cell) + (Cell / 2);
                block[row + cx] = _relief.SampleRoutingElevation(wx, wy);
            }
        }

        return block;
    }

    private static void TrimCache<T>(
        ConcurrentDictionary<CacheKey, Lazy<T>> cache,
        ConcurrentQueue<CacheKey> order,
        CacheKey keep,
        int max)
    {
        int guard = 0;
        while (cache.Count >= max && guard < 8 && order.TryDequeue(out CacheKey oldest))
        {
            guard++;
            if (oldest == keep)
            {
                order.Enqueue(oldest);
                continue;
            }

            _ = cache.TryRemove(oldest, out _);
        }
    }

    private void SampleGrid(int gx0, int gy0, int g, float[] destination)
    {
        int bx0 = FloorDiv(gx0, BlockCells);
        int by0 = FloorDiv(gy0, BlockCells);
        int bx1 = FloorDiv(gx0 + g - 1, BlockCells);
        int by1 = FloorDiv(gy0 + g - 1, BlockCells);

        int bw = bx1 - bx0 + 1;
        int totalBlocks = bw * (by1 - by0 + 1);
        _ = Parallel.For(0, totalBlocks, i => _ = this.GetBlock(bx0 + (i % bw), by0 + (i / bw)));

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
                    Array.Copy(block, src, destination, dst, cx1 - cx0);
                }
            }
        }
    }

    private RegionData BuildRegion(int rx, int ry)
    {
        int x0 = rx * RegionSize;
        int y0 = ry * RegionSize;

        int margin = MaxRiverLength + FillApron;
        int gx0 = FloorDiv(x0 - margin, Cell);
        int gy0 = FloorDiv(y0 - margin, Cell);
        int g = ((RegionSize + (2 * margin)) / Cell) + 1;
        int cellCount = g * g;

        float[] relief = ArrayPool<float>.Shared.Rent(cellCount);
        float[] filled = ArrayPool<float>.Shared.Rent(cellCount);
        bool[] done = ArrayPool<bool>.Shared.Rent(cellCount);
        int[] flow = ArrayPool<int>.Shared.Rent(cellCount);
        int[] next = ArrayPool<int>.Shared.Rent(cellCount);
        float[] mouth = ArrayPool<float>.Shared.Rent(cellCount);
        int[] prev = ArrayPool<int>.Shared.Rent(cellCount);
        bool[] isLake = ArrayPool<bool>.Shared.Rent(cellCount);

        try
        {
            this.SampleGrid(gx0, gy0, g, relief);
            Fill(relief, filled, done, g, cellCount);

            Array.Clear(flow, 0, cellCount);
            Array.Fill(next, -1, 0, cellCount);
            Array.Fill(mouth, 1f, 0, cellCount);
            Array.Fill(prev, -1, 0, cellCount);
            Array.Clear(isLake, 0, cellCount);

            List<(float Relief, int Cx, int Cy)> sources = new(128);
            List<int> lakeEnds = new(32);

            int s0x = FloorDiv(x0 - MaxRiverLength, SourceSpacing);
            int s0y = FloorDiv(y0 - MaxRiverLength, SourceSpacing);
            int s1x = FloorDiv(x0 + RegionSize + MaxRiverLength, SourceSpacing);
            int s1y = FloorDiv(y0 + RegionSize + MaxRiverLength, SourceSpacing);

            for (int sy = s0y; sy <= s1y; sy++)
            {
                for (int sx = s0x; sx <= s1x; sx++)
                {
                    ulong h = SpatialHash.Hash(_seed ^ 0x5D1C3A7B9E2F4061UL, sx, sy);
                    if ((h & 0xFF) >= 136)
                    {
                        continue; // ~53% of candidates yield springs
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
                    if (r is >= MinSourceRelief and <= MaxSourceRelief)
                    {
                        sources.Add((r, cx, cy));
                    }
                }
            }

            sources.Sort((a, b) => b.Relief != a.Relief ? b.Relief.CompareTo(a.Relief) : (a.Cy != b.Cy ? a.Cy.CompareTo(b.Cy) : a.Cx.CompareTo(b.Cx)));

            HashSet<int> sourceCells = new(sources.Count);
            List<int> path = new(64);
            for (int si = 0; si < sources.Count; si++)
            {
                (_, int cx, int cy) = sources[si];
                int start = (cy * g) + cx;
                if (flow[start] > 0 || !sourceCells.Add(start))
                {
                    continue;
                }

                path.Clear();
                TracePureDownhill(filled, flow, g, cx, cy, path, out bool endsInLake);
                if (path.Count < 2)
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
                else if (filled[path[^1]] < SeaRelief)
                {
                    for (int k = 0; k < Math.Min(EstuaryCells, path.Count); k++)
                    {
                        int c = path[path.Count - 1 - k];
                        mouth[c] = MathF.Max(mouth[c], 1f + (0.45f * (EstuaryCells - k)));
                    }
                }

                int guard = 0;
                for (int c = start; c >= 0 && guard < 4 * g; c = next[c], guard++)
                {
                    flow[c]++;
                }
            }

            byte[] field = new byte[RegionSize * RegionSize];
            byte[] deepMask = new byte[RegionSize * RegionSize / 8];
            Array.Fill(field, FarAway);

            for (int c = 0; c < cellCount; c++)
            {
                int n = next[c];
                if (n >= 0 && (prev[n] < 0 || flow[c] > flow[prev[n]] || (flow[c] == flow[prev[n]] && c < prev[n])))
                {
                    prev[n] = c;
                }
            }

            // Bounded depression lakes along river courses (prevents cross-region infinite flood-fill seams).
            Stack<int> pending = new();
            for (int c = 0; c < cellCount; c++)
            {
                float d0 = filled[c] - relief[c];
                if (flow[c] == 0 || isLake[c] || d0 < LakeDepth || d0 > MaxLakeDepth)
                {
                    continue;
                }

                int originX = c % g;
                int originY = c / g;
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
                        if (nx < 0 || ny < 0 || nx >= g || ny >= g)
                        {
                            continue;
                        }

                        if (Math.Max(Math.Abs(nx - originX), Math.Abs(ny - originY)) > MaxLakeRadiusCells)
                        {
                            continue;
                        }

                        int n = (ny * g) + nx;
                        float dn = filled[n] - relief[n];
                        if (!isLake[n] && dn >= LakeDepth && dn <= MaxLakeDepth)
                        {
                            isLake[n] = true;
                            pending.Push(n);
                        }
                    }
                }
            }

            // The shallow rim of a lake basin (flooded, but shallower than LakeDepth) joins the lake for a couple
            // of cells. Rivers crossing a filled flat follow the fill's epsilon gradient in rigid straight D8 lines;
            // on a rim left dry they showed as long ruler-straight channels running beside the lake shore.
            List<int> rim = new(64);
            for (int pass = 0; pass < LakeRimCells; pass++)
            {
                rim.Clear();
                for (int c = 0; c < cellCount; c++)
                {
                    float dc = filled[c] - relief[c];
                    if (isLake[c] || dc < FloodedDepth || dc > MaxLakeDepth)
                    {
                        continue;
                    }

                    int cx = c % g;
                    int cy = c / g;
                    if ((cx > 0 && isLake[c - 1]) || (cx < g - 1 && isLake[c + 1]) ||
                        (cy > 0 && isLake[c - g]) || (cy < g - 1 && isLake[c + g]))
                    {
                        rim.Add(c);
                    }
                }

                for (int i = 0; i < rim.Count; i++)
                {
                    isLake[rim[i]] = true;
                }
            }

            const int Steps = 6;
            for (int a = 0; a < cellCount; a++)
            {
                int b = next[a];
                if (b < 0 || (isLake[a] && isLake[b]))
                {
                    continue; // no channel inside a lake: the lake surface covers it
                }

                int p0 = prev[a] >= 0 ? prev[a] : a;
                int p3 = next[b] >= 0 ? next[b] : b;
                (float x0p, float y0p) = this.CellPoint(p0, g, gx0, gy0);
                (float ax, float ay) = this.CellPoint(a, g, gx0, gy0);
                (float bx, float by) = this.CellPoint(b, g, gx0, gy0);
                (float x3p, float y3p) = this.CellPoint(p3, g, gx0, gy0);
                float wa = MathF.Min(Width(flow[a]) * mouth[a], MaxChannelScale);
                float wb = MathF.Min(Width(flow[b]) * mouth[b], MaxChannelScale);
                bool deepSegment = flow[a] >= 4 || mouth[a] > 1.2f || flow[b] >= 4 || mouth[b] > 1.2f;

                float sx = ax;
                float sy = ay;
                for (int i = 1; i <= Steps; i++)
                {
                    float t = i / (float)Steps;
                    float ex = CatmullRom(x0p, ax, bx, x3p, t);
                    float ey = CatmullRom(y0p, ay, by, y3p, t);
                    this.StampSegment(field, deepMask, x0, y0, sx, sy, ex, ey, wa + ((wb - wa) * (i - 1) / Steps), wa + ((wb - wa) * i / Steps), deepSegment);
                    sx = ex;
                    sy = ey;
                }
            }

            this.StampLakeShores(field, x0, y0, gx0, gy0, g, cellCount, filled, relief, isLake);

            for (int i = 0; i < lakeEnds.Count; i++)
            {
                int end = lakeEnds[i];
                (float lx, float ly) = this.CellPoint(end, g, gx0, gy0);
                StampLake(field, x0, y0, lx, ly, MathF.Min(3.0f + (1.4f * MathF.Sqrt(flow[end])), 14f));
            }

            return new RegionData(field, deepMask);
        }
        finally
        {
            ArrayPool<float>.Shared.Return(relief);
            ArrayPool<float>.Shared.Return(filled);
            ArrayPool<bool>.Shared.Return(done);
            ArrayPool<int>.Shared.Return(flow);
            ArrayPool<int>.Shared.Return(next);
            ArrayPool<float>.Shared.Return(mouth);
            ArrayPool<int>.Shared.Return(prev);
            ArrayPool<bool>.Shared.Return(isLake);
        }
    }

    private static bool TouchesLand(bool[] done, int c, int g)
    {
        int cx = c % g;
        int cy = c / g;
        for (int i = 0; i < s_d8.Length; i++)
        {
            int nx = cx + s_d8[i].Dx;
            int ny = cy + s_d8[i].Dy;
            if (nx >= 0 && ny >= 0 && nx < g && ny < g && !done[(ny * g) + nx])
            {
                return true;
            }
        }

        return false;
    }

    private static void Fill(float[] relief, float[] filled, bool[] done, int g, int cellCount)
    {
        Array.Clear(done, 0, cellCount);
        PriorityQueue<int, float> open = new(cellCount / 8);

        for (int c = 0; c < cellCount; c++)
        {
            int cx = c % g;
            int cy = c / g;
            if (cx == 0 || cy == 0 || cx == g - 1 || cy == g - 1 || relief[c] < SeaRelief)
            {
                filled[c] = relief[c];
                done[c] = true;
            }
        }

        for (int c = 0; c < cellCount; c++)
        {
            if (done[c] && TouchesLand(done, c, g))
            {
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
    }

    // Pure steepest descent on filled elevation guarantees identical routing paths regardless of which upstream
    // sources exist in a neighboring region's window.
    private static void TracePureDownhill(
        float[] filled,
        int[] flow,
        int g,
        int cx,
        int cy,
        List<int> path,
        out bool endsInLake)
    {
        path.Add((cy * g) + cx);
        endsInLake = false;
        int maxSteps = MaxRiverLength / Cell;

        for (int step = 0; step < maxSteps; step++)
        {
            float here = filled[(cy * g) + cx];
            if (here < SeaRelief)
            {
                return;
            }

            int bestX = -1;
            int bestY = -1;
            float bestSlope = 0f;
            for (int i = 0; i < s_d8.Length; i++)
            {
                int nx = cx + s_d8[i].Dx;
                int ny = cy + s_d8[i].Dy;
                if (nx < 0 || ny < 0 || nx >= g || ny >= g)
                {
                    continue;
                }

                float nr = filled[(ny * g) + nx];
                if (nr >= here)
                {
                    continue;
                }

                float slope = (here - nr) / s_d8[i].Len;
                if (slope > bestSlope)
                {
                    bestSlope = slope;
                    bestX = nx;
                    bestY = ny;
                }
            }

            if (bestX < 0)
            {
                return;
            }

            cx = bestX;
            cy = bestY;
            int idx = (cy * g) + cx;
            path.Add(idx);

            if (flow[idx] > 0)
            {
                return;
            }
        }

        endsInLake = true;
    }

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

    private static readonly float[] s_widths = Array.ConvertAll(
        [
            0x3F59999A, 0x3FA28F5C, 0x3FC201F1, 0x3FD851EC, 0x3FE9A079, 0x3FF7C481,
            0x4001DC98, 0x40070A3E, 0x400B9B8A, 0x400FB184, 0x401363B8, 0x4016C388,
            0x4019DE2A, 0x401CBDE0, 0x401F6ACF, 0x4021EB85, 0x40244560, 0x40266666,
        ],
        BitConverter.Int32BitsToSingle);

    private static float Width(int flow) => s_widths[Math.Clamp(flow, 1, s_widths.Length) - 1];

    private void StampSegment(
        byte[] field,
        byte[] deepMask,
        int x0,
        int y0,
        float ax,
        float ay,
        float bx,
        float by,
        float wa,
        float wb,
        bool allowDeepCore)
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
            int worldY = y0 + ty;
            float py = worldY + 0.5f;
            int row = ty * RegionSize;
            for (int tx = minX; tx <= maxX; tx++)
            {
                int worldX = x0 + tx;
                float px = worldX + 0.5f;
                float t = Math.Clamp((((px - ax) * dx) + ((py - ay) * dy)) / lenSq, 0f, 1f);
                float ex = px - (ax + (t * dx));
                float ey = py - (ay + (t * dy));
                float d = MathF.Sqrt((ex * ex) + (ey * ey)) / (wa + ((wb - wa) * t));
                int index = row + tx;
                Store(field, index, d);

                if (allowDeepCore && d <= 0.85f)
                {
                    // Natural shallow fords break up deep trunks into wadeable crossings every ~8 coarse zones.
                    bool isNaturalFord = (SpatialHash.Hash32(_seed, worldX >> 4, worldY >> 4, 0xF08DUL) & 7) == 0;
                    if (!isNaturalFord)
                    {
                        deepMask[index >> 3] |= (byte)(1 << (index & 7));
                    }
                }
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
            float eyBase = y0 + ty + 0.5f - ly;
            int row = ty * RegionSize;
            for (int tx = minX; tx <= maxX; tx++)
            {
                float ex = x0 + tx + 0.5f - lx;
                float d = MathF.Max(MathF.Sqrt((ex * ex) + (eyBase * eyBase)) - radius, 0f);
                Store(field, row + tx, d);
            }
        }
    }

    private void StampLakeShores(
        byte[] field,
        int x0,
        int y0,
        int gx0,
        int gy0,
        int g,
        int cellCount,
        float[] filled,
        float[] relief,
        bool[] isLake)
    {
        const float Threshold = LakeDepth * 0.5f;

        float[] depth = ArrayPool<float>.Shared.Rent(cellCount);
        float[] smooth = ArrayPool<float>.Shared.Rent(cellCount);
        try
        {
            Array.Clear(depth, 0, cellCount);
            Array.Clear(smooth, 0, cellCount);

            int minCx = g;
            int minCy = g;
            int maxCx = -1;
            int maxCy = -1;
            for (int c = 0; c < cellCount; c++)
            {
                if (!isLake[c])
                {
                    continue;
                }

                // Every lake cell is at least LakeDepth deep, so the shore encloses the shallow rim as well.
                depth[c] = MathF.Max(filled[c] - relief[c], LakeDepth);
                minCx = Math.Min(minCx, c % g);
                maxCx = Math.Max(maxCx, c % g);
                minCy = Math.Min(minCy, c / g);
                maxCy = Math.Max(maxCy, c / g);
            }

            if (maxCx < 0)
            {
                return;
            }

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

            // Reuse depth as "some lake within one cell": tiles far from any lake skip the warp noise.
            float[] near = depth;
            Array.Clear(near, 0, cellCount);
            for (int cy = Math.Max(minCy - 3, 1); cy <= Math.Min(maxCy + 3, g - 2); cy++)
            {
                for (int cx = Math.Max(minCx - 3, 1); cx <= Math.Min(maxCx + 3, g - 2); cx++)
                {
                    float m = 0f;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            m = MathF.Max(m, smooth[((cy + dy) * g) + cx + dx]);
                        }
                    }

                    near[(cy * g) + cx] = m;
                }
            }

            int tx0 = Math.Max(((gx0 + minCx - 3) * Cell) - x0, 0);
            int tx1 = Math.Min(((gx0 + maxCx + 4) * Cell) - x0, RegionSize - 1);
            int ty0 = Math.Max(((gy0 + minCy - 3) * Cell) - y0, 0);
            int ty1 = Math.Min(((gy0 + maxCy + 4) * Cell) - y0, RegionSize - 1);
            for (int ty = ty0; ty <= ty1; ty++)
            {
                float wy = y0 + ty + 0.5f;
                for (int tx = tx0; tx <= tx1; tx++)
                {
                    // The cell grid is coarse (16 tiles): warping the sample point with world-space noise bends
                    // shores that would otherwise follow straight cell rows and columns.
                    float wx = x0 + tx + 0.5f;
                    int ncx = (int)MathF.Floor(((wx - (Cell * 0.5f)) / Cell) - gx0);
                    int ncy = (int)MathF.Floor(((wy - (Cell * 0.5f)) / Cell) - gy0);
                    if (ncx < 0 || ncy < 0 || ncx >= g - 1 || ncy >= g - 1 ||
                        (near[(ncy * g) + ncx] <= 0f && near[(ncy * g) + ncx + 1] <= 0f &&
                         near[((ncy + 1) * g) + ncx] <= 0f && near[((ncy + 1) * g) + ncx + 1] <= 0f))
                    {
                        continue;
                    }

                    float warpX = _shoreWarp.Evaluate(wx * ShoreWarpFrequency, wy * ShoreWarpFrequency) * ShoreWarpCells;
                    float warpY = _shoreWarp.Evaluate((wx * ShoreWarpFrequency) + 71.3f, (wy * ShoreWarpFrequency) - 37.9f) * ShoreWarpCells;
                    float v = ((wy - (Cell * 0.5f)) / Cell) - gy0 + warpY;
                    int cy = (int)MathF.Floor(v);
                    float u = ((wx - (Cell * 0.5f)) / Cell) - gx0 + warpX;
                    int cx = (int)MathF.Floor(u);
                    if (cx < 0 || cx >= g - 1 || cy < 0 || cy >= g - 1)
                    {
                        continue;
                    }

                    float fy = v - cy;
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

                    float gx = (d10 - d00 + ((d11 - d01 - (d10 - d00)) * fy)) / Cell;
                    float gy = (bottom - top) / Cell;
                    float grad = MathF.Max(MathF.Sqrt((gx * gx) + (gy * gy)), 1e-6f);
                    Store(field, (ty * RegionSize) + tx, 2.25f + ((Threshold - d) / grad));
                }
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(depth);
            ArrayPool<float>.Shared.Return(smooth);
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
