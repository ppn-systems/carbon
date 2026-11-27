// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using Carbon.Abstractions.World;

namespace Carbon.Host;

/// <summary>
/// Reuses the structure-of-arrays columns of <c>EntityDeltaBatch</c> between sends (audit round 2, PERF-3).
/// The packet serializes each array by its <see cref="Array.Length"/>, so the columns must be exactly as long as the
/// batch. They are therefore pooled per entry count rather than rented from <c>ArrayPool</c>, which would hand out
/// longer arrays and put garbage on the wire. A stable AoI sends the same few counts every tick, so after warm-up a
/// delta batch allocates no columns.
/// </summary>
/// <remarks>Thread-safe: columns are rented on the tick thread and returned when the async send completes.</remarks>
internal sealed class DeltaColumnPool
{
    /// <summary>Batches with more entries than this are not pooled (they allocate, as before).</summary>
    internal const int MaxPooledCount = 512;

    /// <summary>Column sets kept per entry count (bounded so a burst cannot pin memory).</summary>
    internal const int MaxSetsPerCount = 64;

    private readonly Stack<DeltaColumns>?[] _byCount = new Stack<DeltaColumns>?[MaxPooledCount + 1];
    private readonly Lock _gate = new();

    /// <summary>Rents columns of exactly <paramref name="entries"/>.Length and copies the entries into them.</summary>
    public DeltaColumns Rent(ReadOnlySpan<EntityDeltaEntry> entries)
    {
        int count = entries.Length;
        DeltaColumns? columns = null;
        if (count == 0)
        {
            return DeltaColumns.Empty;
        }

        if (count <= MaxPooledCount)
        {
            lock (_gate)
            {
                Stack<DeltaColumns>? stack = _byCount[count];
                if (stack is not null && stack.Count > 0)
                {
                    columns = stack.Pop();
                }
            }
        }

        columns ??= new DeltaColumns(count);
        columns.CopyFrom(entries);
        return columns;
    }

    /// <summary>Returns columns rented from <see cref="Rent"/>. The caller must no longer reference their arrays.</summary>
    public void Return(DeltaColumns columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        int count = columns.Count;
        if (count == 0 || count > MaxPooledCount)
        {
            return;
        }

        lock (_gate)
        {
            Stack<DeltaColumns> stack = _byCount[count] ??= new Stack<DeltaColumns>();
            if (stack.Count < MaxSetsPerCount)
            {
                stack.Push(columns);
            }
        }
    }
}

/// <summary>One set of exact-length <c>EntityDeltaBatch</c> columns.</summary>
internal sealed class DeltaColumns
{
    private DeltaColumns()
    {
        this.Ids = [];
        this.PositionsX = [];
        this.PositionsY = [];
        this.VelocitiesX = [];
        this.VelocitiesY = [];
        this.Healths = [];
    }

    internal DeltaColumns(int count)
    {
        this.Ids = new ulong[count];
        this.PositionsX = new float[count];
        this.PositionsY = new float[count];
        this.VelocitiesX = new float[count];
        this.VelocitiesY = new float[count];
        this.Healths = new float[count];
    }

    /// <summary>The shared, never-written columns of an empty batch.</summary>
    public static DeltaColumns Empty { get; } = new();

    public int Count => this.Ids.Length;

    public ulong[] Ids { get; }

    public float[] PositionsX { get; }

    public float[] PositionsY { get; }

    public float[] VelocitiesX { get; }

    public float[] VelocitiesY { get; }

    public float[] Healths { get; }

    internal void CopyFrom(ReadOnlySpan<EntityDeltaEntry> entries)
    {
        for (int i = 0; i < entries.Length; i++)
        {
            ref readonly EntityDeltaEntry e = ref entries[i];
            this.Ids[i] = e.NetworkId;
            this.PositionsX[i] = e.PositionX;
            this.PositionsY[i] = e.PositionY;
            this.VelocitiesX[i] = e.VelocityX;
            this.VelocitiesY[i] = e.VelocityY;
            this.Healths[i] = e.CurrentHealth;
        }
    }
}
