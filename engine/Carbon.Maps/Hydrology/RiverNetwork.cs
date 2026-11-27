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
/// <remarks>
/// Per 512-tile region it stores, for every tile, the width-scaled distance to the nearest river or lake and the
/// depth of that water, plus a coarse distance-to-coast field the sea depth is read from (ADR-032). Rivers widen
/// with the number of springs upstream, are as deep as they are wide, and always end in the sea, in a lake or in
/// another river.
/// </remarks>
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
    // Rivers and lakes are stamped out to this width-scaled distance: past every threshold a reader uses (harbours
    // look within 12, the riparian valley ends at 10). Farther tiles read as far from any river.
    private const float MaxStampedDistance = 12.5f;

    private const int MaxCachedRegions = 128;

    // Regions held beyond the pinned ones, for those being streamed in around a moving player.
    private const int PinMargin = 16;
    private const int MaxCachedBlocks = 640;

    private const int EstuaryCells = 3;

    // Polyline pieces per cell-to-cell stretch of river.
    private const int CurveSteps = 6;

    // Channel half-width in tiles: BaseHalfWidth + HalfWidthPerSqrtFlow * sqrt(springs upstream), at most MaxHalfWidth.
    private const float BaseHalfWidth = 2.2f;
    private const float HalfWidthPerSqrtFlow = 1.7f;
    private const float MaxHalfWidth = 18f;

    // The pool a river ends in (when it reaches neither the sea nor another river) is at least this wide.
    private const float MinPoolRadius = 6f;

    // Beyond the water, banks are measured in widths of at most this many (old widest river): wide rivers do not
    // carve proportionally wider valleys.
    private const float MinBankScale = 0.85f;
    private const float MaxBankScale = 2.6f;

    // Coast distance: cells of apron around the region (beyond it the distance is capped), in 16-tile cells.
    private const int CoastApronCells = 16;
    private const int CoastCellsPerSide = (RegionSize / Cell) + 2;
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

    private readonly record struct RegionData(byte[] DistanceField, byte[] DepthField, float[] CoastDistance);

    private readonly record struct CacheKey(int X, int Y);

    // A stream's latest prewarm rectangle, in regions (ADR-045 D7).
    private readonly record struct PinRect(int X0, int Y0, int X1, int Y1)
    {
        public int Count => (this.X1 - this.X0 + 1) * (this.Y1 - this.Y0 + 1);

        public bool Contains(CacheKey key) => key.X >= this.X0 && key.X <= this.X1 && key.Y >= this.Y0 && key.Y <= this.Y1;
    }

    private static readonly (int Dx, int Dy, float Len)[] s_d8 =
    [
        (1, 0, 1f), (-1, 0, 1f), (0, 1, 1f), (0, -1, 1f),
        (1, 1, 1.4142135f), (1, -1, 1.4142135f), (-1, 1, 1.4142135f), (-1, -1, 1.4142135f),
    ];

    // ADR-045 D7: the caches belong to this network, so to one world. Two worlds never trim each other's regions.
    private readonly ConcurrentDictionary<CacheKey, Lazy<RegionData>> _regions = new();
    private readonly ConcurrentQueue<CacheKey> _regionOrder = new();
    private readonly ConcurrentDictionary<CacheKey, Lazy<float[]>> _blocks = new();
    private readonly ConcurrentQueue<CacheKey> _blockOrder = new();

    // Regions within a stream's latest prewarm rectangle are pinned: trimming skips them, and the bound grows to hold them.
    private readonly Lock _pinGate = new();
    private readonly Dictionary<ulong, PinRect> _pins = [];
    private int _pinnedRegions;

    // ADR-045 D10: background builds started by Prewarm, each observed; StopBackgroundBuilds waits for them.
    private readonly ConcurrentDictionary<Task, byte> _builds = new();
    private int _stopped;

    private readonly ulong _seed;
    private readonly int _maxCachedRegions;
    private int _regionBuilds;
    private readonly IRoutingTerrain _relief;
    private readonly SimplexNoise2D _shoreWarp;

    /// <summary>Initializes a new instance of the <see cref="RiverNetwork"/> class.</summary>
    /// <param name="seed">World seed.</param>
    /// <param name="relief">Terrain elevation used for routing at a world tile; must be deterministic.</param>
    public RiverNetwork(ulong seed, IRoutingTerrain relief)
        : this(seed, relief, MaxCachedRegions)
    {
    }

    /// <summary>Initializes a new instance with a region bound of <paramref name="maxCachedRegions"/> (tests).</summary>
    internal RiverNetwork(ulong seed, IRoutingTerrain relief, int maxCachedRegions)
    {
        _maxCachedRegions = maxCachedRegions;
        _seed = seed;
        _relief = relief ?? throw new ArgumentNullException(nameof(relief));
        _shoreWarp = new SimplexNoise2D(seed ^ 0x6C8E9CF570932BD5UL);
    }

    /// <summary>Gets the number of regions this network has built (tests: a cache hit builds none).</summary>
    internal int RegionBuilds => Volatile.Read(ref _regionBuilds);

    /// <summary>Gets the number of regions cached (tests: the bound and the pins).</summary>
    internal int CachedRegions => _regions.Count;

    /// <summary>Gets the number of background builds not finished yet (tests).</summary>
    internal int PendingBuilds => _builds.Count;

    /// <inheritdoc/>
    public float GetDistance(int worldX, int worldY) =>
        this.GetDistance(worldX, worldY, out _);

    /// <inheritdoc/>
    public float GetDistance(int worldX, int worldY, out float waterDepth)
    {
        int rx = FloorDiv(worldX, RegionSize);
        int ry = FloorDiv(worldY, RegionSize);
        RegionData data = this.GetRegion(rx, ry);
        int index = ((worldY - (ry * RegionSize)) * RegionSize) + (worldX - (rx * RegionSize));
        byte q = data.DistanceField[index];
        float distance = q == FarAway ? float.MaxValue : q / Quantum;
        waterDepth = distance <= WorldConstants.RiverChannel
            ? MathF.Max(data.DepthField[index] * (WaterDepth.MaxInlandDepth / 255f), WaterDepth.MinDepth)
            : 0f;
        return distance;
    }

    /// <inheritdoc/>
    public float GetCoastDistance(int worldX, int worldY)
    {
        int rx = FloorDiv(worldX, RegionSize);
        int ry = FloorDiv(worldY, RegionSize);
        float[] coast = this.GetRegion(rx, ry).CoastDistance;

        // Cell c (relative to the region, starting one cell before it) is centred on tile (c - 1) * Cell + Cell / 2.
        float u = ((worldX - (rx * RegionSize) + 0.5f - (Cell * 0.5f)) / Cell) + 1f;
        float v = ((worldY - (ry * RegionSize) + 0.5f - (Cell * 0.5f)) / Cell) + 1f;
        int cx = Math.Clamp((int)MathF.Floor(u), 0, CoastCellsPerSide - 2);
        int cy = Math.Clamp((int)MathF.Floor(v), 0, CoastCellsPerSide - 2);
        float fx = Math.Clamp(u - cx, 0f, 1f);
        float fy = Math.Clamp(v - cy, 0f, 1f);
        int i = (cy * CoastCellsPerSide) + cx;
        float top = coast[i] + ((coast[i + 1] - coast[i]) * fx);
        float bottom = coast[i + CoastCellsPerSide] + ((coast[i + CoastCellsPerSide + 1] - coast[i + CoastCellsPerSide]) * fx);

        // Distances run between cell centres; the coast itself lies about half a cell out from the last land centre.
        return MathF.Max(top + ((bottom - top) * fy) - (Cell * 0.5f), 0f);
    }

    /// <summary>
    /// Starts building every region within <paramref name="radiusTiles"/> on the thread pool if not already cached, and
    /// pins them for <paramref name="stream"/> (one per player on the server) until its next prewarm moves elsewhere or
    /// <see cref="ReleaseStream"/> drops it.
    /// </summary>
    public void Prewarm(int worldX, int worldY, int radiusTiles, ulong stream = 0)
    {
        int rx0 = FloorDiv(worldX - radiusTiles, RegionSize);
        int rx1 = FloorDiv(worldX + radiusTiles, RegionSize);
        int ry0 = FloorDiv(worldY - radiusTiles, RegionSize);
        int ry1 = FloorDiv(worldY + radiusTiles, RegionSize);
        this.Pin(stream, new PinRect(rx0, ry0, rx1, ry1));
        if (Volatile.Read(ref _stopped) != 0)
        {
            return;
        }

        for (int ry = ry0; ry <= ry1; ry++)
        {
            for (int rx = rx0; rx <= rx1; rx++)
            {
                CacheKey key = new(rx, ry);
                if (_regions.ContainsKey(key))
                {
                    continue;
                }

                Lazy<RegionData> lazy = this.GetOrRegisterRegionLazy(key, rx, ry);
                if (!lazy.IsValueCreated)
                {
                    this.StartBuild(lazy);
                }
            }
        }
    }

    /// <summary>Unpins the regions of <paramref name="stream"/> (a player left the world).</summary>
    public void ReleaseStream(ulong stream)
    {
        lock (_pinGate)
        {
            if (_pins.Remove(stream, out PinRect old))
            {
                _pinnedRegions -= old.Count;
            }
        }
    }

    /// <summary>
    /// ADR-045 D10: stops background builds on shutdown. Builds not started yet are skipped, running ones stop at their
    /// next check, and every one is awaited, so none is left running or unobserved.
    /// </summary>
    /// <param name="timeout">How long to wait for running builds.</param>
    /// <returns><see langword="true"/> if every build finished within <paramref name="timeout"/>.</returns>
    public bool StopBackgroundBuilds(TimeSpan timeout)
    {
        _ = Interlocked.Exchange(ref _stopped, 1);
        Task[] pending = [.. _builds.Keys];
        try
        {
            return Task.WaitAll(pending, timeout);
        }
        catch (AggregateException)
        {
            // A cancelled or failed build already ran its observing continuation; nothing is left unobserved.
            return true;
        }
    }

    private void Pin(ulong stream, PinRect rect)
    {
        lock (_pinGate)
        {
            if (_pins.TryGetValue(stream, out PinRect old))
            {
                if (old == rect)
                {
                    return;
                }

                _pinnedRegions -= old.Count;
            }

            _pins[stream] = rect;
            _pinnedRegions += rect.Count;
        }
    }

    private bool IsPinned(CacheKey key)
    {
        lock (_pinGate)
        {
            foreach (PinRect rect in _pins.Values)
            {
                if (rect.Contains(key))
                {
                    return true;
                }
            }

            return false;
        }
    }

    // Kept out of Prewarm: a lambda capturing a loop local makes the compiler allocate its closure at the top of every
    // loop iteration, including the common already-cached case. Here it is allocated only when a build actually starts.
    private void StartBuild(Lazy<RegionData> lazy)
    {
        Task build = Task.Run(() => _ = lazy.Value);
        _ = _builds.TryAdd(build, 0);
        _ = build.ContinueWith(
            static (t, state) =>
            {
                _ = t.Exception; // observed: a build stopped by StopBackgroundBuilds never surfaces as unobserved
                _ = ((ConcurrentDictionary<Task, byte>)state!).TryRemove(t, out _);
            },
            _builds,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private RegionData GetRegion(int rx, int ry)
    {
        CacheKey key = new(rx, ry);
        if (_regions.TryGetValue(key, out Lazy<RegionData>? existing))
        {
            return existing.Value;
        }

        return this.GetOrRegisterRegionLazy(key, rx, ry).Value;
    }

    private Lazy<RegionData> GetOrRegisterRegionLazy(CacheKey key, int rx, int ry)
    {
        // The bound holds every pinned region plus a margin for those being streamed in.
        int bound = Math.Max(_maxCachedRegions, Volatile.Read(ref _pinnedRegions) + PinMargin);
        this.TrimRegions(key, bound);
        Lazy<RegionData> created = new(() => this.BuildRegion(rx, ry), LazyThreadSafetyMode.ExecutionAndPublication);
        Lazy<RegionData> active = _regions.GetOrAdd(key, created);
        if (ReferenceEquals(active, created))
        {
            _regionOrder.Enqueue(key);
        }

        return active;
    }

    private float[] GetBlock(int bx, int by)
    {
        CacheKey key = new(bx, by);
        if (_blocks.TryGetValue(key, out Lazy<float[]>? existing))
        {
            return existing.Value;
        }

        TrimCache(_blocks, _blockOrder, key, MaxCachedBlocks);
        Lazy<float[]> created = new(() => this.BuildBlock(bx, by), LazyThreadSafetyMode.ExecutionAndPublication);
        Lazy<float[]> active = _blocks.GetOrAdd(key, created);
        if (ReferenceEquals(active, created))
        {
            _blockOrder.Enqueue(key);
        }

        return active.Value;
    }

    // First in, first out, skipping pinned regions and the one being added.
    private void TrimRegions(CacheKey keep, int max)
    {
        int guard = 0;
        while (_regions.Count >= max && guard < 8 && _regionOrder.TryDequeue(out CacheKey oldest))
        {
            guard++;
            if (oldest == keep || this.IsPinned(oldest))
            {
                _regionOrder.Enqueue(oldest);
                continue;
            }

            _ = _regions.TryRemove(oldest, out _);
        }
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

    private void ThrowIfStopped()
    {
        if (Volatile.Read(ref _stopped) != 0)
        {
            throw new OperationCanceledException("The river network was stopped.");
        }
    }

    private RegionData BuildRegion(int rx, int ry)
    {
        // ADR-045 D10: a build not started before shutdown is skipped; one running stops at its next check. The caller
        // waiting on it sees the cancellation, and only while the world is stopping.
        this.ThrowIfStopped();

        _ = Interlocked.Increment(ref _regionBuilds);
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
            this.ThrowIfStopped();
            float[] coast = BuildCoastDistance(relief, g, FloorDiv(x0, Cell) - gx0, FloorDiv(y0, Cell) - gy0);
            Fill(relief, filled, done, g, cellCount);
            this.ThrowIfStopped();

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
            byte[] depthField = new byte[RegionSize * RegionSize];
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

            Span<float> curveX = stackalloc float[CurveSteps + 1];
            Span<float> curveY = stackalloc float[CurveSteps + 1];

            // Only stretches starting within this many cells of the region can reach into it (a stretch spans
            // at most two cells, plus its widest reach); most of the routing grid lies outside the region.
            const int StampCells = 2 + (int)((MaxHalfWidth + ((MaxStampedDistance - WorldConstants.RiverChannel) * MaxBankScale)) / Cell) + 1;
            int regionCx = FloorDiv(x0, Cell) - gx0;
            int regionCy = FloorDiv(y0, Cell) - gy0;
            for (int a = 0; a < cellCount; a++)
            {
                int b = next[a];
                if (b < 0 || (isLake[a] && isLake[b]))
                {
                    continue; // no channel inside a lake: the lake surface covers it
                }

                int acx = a % g;
                int acy = a / g;
                if (acx < regionCx - StampCells || acx > regionCx + (RegionSize / Cell) + StampCells ||
                    acy < regionCy - StampCells || acy > regionCy + (RegionSize / Cell) + StampCells)
                {
                    continue;
                }

                int p0 = prev[a] >= 0 ? prev[a] : a;
                int p3 = next[b] >= 0 ? next[b] : b;
                (float x0p, float y0p) = this.CellPoint(p0, g, gx0, gy0);
                (float ax, float ay) = this.CellPoint(a, g, gx0, gy0);
                (float bx, float by) = this.CellPoint(b, g, gx0, gy0);
                (float x3p, float y3p) = this.CellPoint(p3, g, gx0, gy0);
                float ha = HalfWidth(flow[a], mouth[a]);
                float hb = HalfWidth(flow[b], mouth[b]);

                for (int i = 0; i <= CurveSteps; i++)
                {
                    float t = i / (float)CurveSteps;
                    curveX[i] = CatmullRom(x0p, ax, bx, x3p, t);
                    curveY[i] = CatmullRom(y0p, ay, by, y3p, t);
                }

                this.StampCurve(field, depthField, x0, y0, curveX, curveY, ha, hb);
            }

            this.StampLakeShores(field, depthField, x0, y0, gx0, gy0, g, cellCount, filled, relief, isLake);

            for (int i = 0; i < lakeEnds.Count; i++)
            {
                int end = lakeEnds[i];
                (float lx, float ly) = this.CellPoint(end, g, gx0, gy0);
                // A pool clearly wider than the river that fills it.
                StampLake(field, depthField, x0, y0, lx, ly, Math.Clamp(1.5f * HalfWidth(flow[end], 1f), MinPoolRadius, MaxHalfWidth));
            }

            return new RegionData(field, depthField, coast);
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
                // Nowhere lower to go (the edge of the routing window): pool here rather than stop dead on land.
                endsInLake = true;
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

    // Rivers widen downstream with the number of springs feeding them, and flare at the estuary.
    private static float HalfWidth(int flow, float mouth) =>
        MathF.Min((BaseHalfWidth + (HalfWidthPerSqrtFlow * MathF.Sqrt(Math.Max(flow, 1)))) * mouth, MaxHalfWidth);

    // Beyond the water's edge, distance is counted in bank widths, so the field keeps its old meaning
    // (≤ RiverChannel is water, RiparianReach is the valley edge) and wide rivers keep natural banks.
    private static float BankScale(float halfWidth) =>
        Math.Clamp(halfWidth / WorldConstants.RiverChannel, MinBankScale, MaxBankScale);

    // Stamps one cell-to-cell stretch of river, a Catmull-Rom curve given as a polyline, in a single pass over its
    // bounding box: each tile is measured against the nearest piece of the polyline (one box, not one per piece,
    // which overlapped about sixfold). The half-width runs linearly from ha to hb along the stretch.
    private void StampCurve(
        byte[] field,
        byte[] depthField,
        int x0,
        int y0,
        ReadOnlySpan<float> curveX,
        ReadOnlySpan<float> curveY,
        float ha,
        float hb)
    {
        float maxHalf = MathF.Max(ha, hb);
        float reach = maxHalf + ((MaxStampedDistance - WorldConstants.RiverChannel) * BankScale(maxHalf));
        float loX = float.MaxValue;
        float hiX = float.MinValue;
        float loY = float.MaxValue;
        float hiY = float.MinValue;
        for (int i = 0; i < curveX.Length; i++)
        {
            loX = MathF.Min(loX, curveX[i]);
            hiX = MathF.Max(hiX, curveX[i]);
            loY = MathF.Min(loY, curveY[i]);
            hiY = MathF.Max(hiY, curveY[i]);
        }

        int minX = Math.Max((int)MathF.Floor(loX - reach) - x0, 0);
        int maxX = Math.Min((int)MathF.Ceiling(hiX + reach) - x0, RegionSize - 1);
        int minY = Math.Max((int)MathF.Floor(loY - reach) - y0, 0);
        int maxY = Math.Min((int)MathF.Ceiling(hiY + reach) - y0, RegionSize - 1);
        if (minX > maxX || minY > maxY)
        {
            return;
        }

        int pieces = curveX.Length - 1;
        float reachSq = reach * reach;

        // Each row only needs the span the curve's reach covers there: the union of discs round the polyline
        // points, widened by the longest piece so the discs cover the pieces between them.
        float longest = 0f;
        for (int i = 0; i < pieces; i++)
        {
            float dx = curveX[i + 1] - curveX[i];
            float dy = curveY[i + 1] - curveY[i];
            longest = MathF.Max(longest, (dx * dx) + (dy * dy));
        }

        float cover = reach + (0.5f * MathF.Sqrt(longest)) + 1f;
        float coverSq = cover * cover;
        for (int ty = minY; ty <= maxY; ty++)
        {
            int worldY = y0 + ty;
            float py = worldY + 0.5f;
            int row = ty * RegionSize;
            float spanLo = float.MaxValue;
            float spanHi = float.MinValue;
            for (int i = 0; i < curveX.Length; i++)
            {
                float oy = py - curveY[i];
                float sq = coverSq - (oy * oy);
                if (sq > 0f)
                {
                    float half = MathF.Sqrt(sq);
                    spanLo = MathF.Min(spanLo, curveX[i] - half);
                    spanHi = MathF.Max(spanHi, curveX[i] + half);
                }
            }

            int rowMinX = Math.Max(minX, (int)MathF.Floor(spanLo) - x0);
            int rowMaxX = Math.Min(maxX, (int)MathF.Ceiling(spanHi) - x0);
            for (int tx = rowMinX; tx <= rowMaxX; tx++)
            {
                int worldX = x0 + tx;
                float px = worldX + 0.5f;

                // The nearest point of the polyline, and how far along the stretch it lies (0..1).
                float bestSq = float.MaxValue;
                float along = 0f;
                for (int i = 0; i < pieces; i++)
                {
                    float ax = curveX[i];
                    float ay = curveY[i];
                    float dx = curveX[i + 1] - ax;
                    float dy = curveY[i + 1] - ay;
                    float t = Math.Clamp((((px - ax) * dx) + ((py - ay) * dy)) / MathF.Max((dx * dx) + (dy * dy), 1e-6f), 0f, 1f);
                    float ex = px - (ax + (t * dx));
                    float ey = py - (ay + (t * dy));
                    float sq = (ex * ex) + (ey * ey);
                    if (sq < bestSq)
                    {
                        bestSq = sq;
                        along = (i + t) / pieces;
                    }
                }

                if (bestSq >= reachSq)
                {
                    continue;
                }

                float dist = MathF.Sqrt(bestSq);
                float half = ha + ((hb - ha) * along);
                int index = row + tx;
                if (dist > half)
                {
                    Store(field, index, WorldConstants.RiverChannel + ((dist - half) / BankScale(half)));
                    continue;
                }

                float across = dist / half;
                Store(field, index, WorldConstants.RiverChannel * across);

                // A parabolic bed, as deep as the channel is wide (one tile less, for the tiles the edge rounds
                // off): zero at the banks, deepest mid-stream.
                float depth = WaterDepth.MaxDepthForChannelWidth((2f * half) - 1f) * (1f - (across * across));
                if (depth >= WaterDepth.MediumFrom &&
                    (SpatialHash.Hash32(_seed, worldX >> 4, worldY >> 4, 0xF08DUL) & 7) == 0)
                {
                    // Natural fords break up big rivers into wadeable crossings every ~8 coarse zones.
                    depth = WaterDepth.MediumFrom * 0.9f;
                }

                StoreDepth(depthField, index, depth);
            }
        }
    }

    private static void StampLake(byte[] field, byte[] depthField, int x0, int y0, float lx, float ly, float radius)
    {
        float centreDepth = WaterDepth.MaxDepthForChannelWidth((2f * radius) - 1f);
        float reach = radius + MaxStampedDistance;
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
                float r = MathF.Sqrt((ex * ex) + (eyBase * eyBase));
                Store(field, row + tx, MathF.Max(r - radius, 0f));
                if (r < radius)
                {
                    float k = r / radius;
                    StoreDepth(depthField, row + tx, centreDepth * (1f - (k * k)));
                }
            }
        }
    }

    private void StampLakeShores(
        byte[] field,
        byte[] depthField,
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
                        // Lakes deepen with the depth of their basin, from the waterline to a deep heart.
                        Store(field, (ty * RegionSize) + tx, 0f);
                        float k = Math.Clamp((d - Threshold) / (MaxLakeDepth - Threshold), 0f, 1f);
                        StoreDepth(depthField, (ty * RegionSize) + tx, WaterDepth.MaxInlandDepth * k);
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
        if (distance >= MaxStampedDistance)
        {
            return;
        }

        byte q = (byte)(distance * Quantum);
        if (q < field[index])
        {
            field[index] = q;
        }
    }

    private static void StoreDepth(byte[] depthField, int index, float depth)
    {
        byte q = (byte)(Math.Clamp(depth / WaterDepth.MaxInlandDepth, 0f, 1f) * 255f);
        if (q > depthField[index])
        {
            depthField[index] = q;
        }
    }

    // Chamfer distance (in tiles) from every cell around the region to the nearest land cell, capped at the apron:
    // the sea gets deeper the farther it is from any coast. The window holds every cell within the cap of the
    // region, so neighbouring regions agree on the cells they share and the sea floor has no seams.
    private static float[] BuildCoastDistance(float[] relief, int g, int regionCx, int regionCy)
    {
        const int Window = CoastCellsPerSide + (2 * CoastApronCells);
        const float Cap = CoastApronCells * Cell;
        const float Straight = Cell;
        const float Diagonal = Cell * 1.4142135f;

        float[] dist = ArrayPool<float>.Shared.Rent(Window * Window);
        try
        {
            int wx0 = regionCx - 1 - CoastApronCells;
            int wy0 = regionCy - 1 - CoastApronCells;
            for (int y = 0; y < Window; y++)
            {
                for (int x = 0; x < Window; x++)
                {
                    int gx = Math.Clamp(wx0 + x, 0, g - 1);
                    int gy = Math.Clamp(wy0 + y, 0, g - 1);
                    dist[(y * Window) + x] = relief[(gy * g) + gx] >= SeaRelief ? 0f : Cap;
                }
            }

            for (int y = 0; y < Window; y++)
            {
                for (int x = 0; x < Window; x++)
                {
                    int i = (y * Window) + x;
                    float d = dist[i];
                    if (x > 0) { d = MathF.Min(d, dist[i - 1] + Straight); }
                    if (y > 0)
                    {
                        d = MathF.Min(d, dist[i - Window] + Straight);
                        if (x > 0) { d = MathF.Min(d, dist[i - Window - 1] + Diagonal); }
                        if (x < Window - 1) { d = MathF.Min(d, dist[i - Window + 1] + Diagonal); }
                    }

                    dist[i] = d;
                }
            }

            for (int y = Window - 1; y >= 0; y--)
            {
                for (int x = Window - 1; x >= 0; x--)
                {
                    int i = (y * Window) + x;
                    float d = dist[i];
                    if (x < Window - 1) { d = MathF.Min(d, dist[i + 1] + Straight); }
                    if (y < Window - 1)
                    {
                        d = MathF.Min(d, dist[i + Window] + Straight);
                        if (x < Window - 1) { d = MathF.Min(d, dist[i + Window + 1] + Diagonal); }
                        if (x > 0) { d = MathF.Min(d, dist[i + Window - 1] + Diagonal); }
                    }

                    dist[i] = d;
                }
            }

            float[] coast = new float[CoastCellsPerSide * CoastCellsPerSide];
            for (int y = 0; y < CoastCellsPerSide; y++)
            {
                Array.Copy(dist, ((y + CoastApronCells) * Window) + CoastApronCells, coast, y * CoastCellsPerSide, CoastCellsPerSide);
            }

            return coast;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(dist);
        }
    }

    private static int FloorDiv(int a, int b) => a >= 0 ? a / b : ((a + 1) / b) - 1;
}
