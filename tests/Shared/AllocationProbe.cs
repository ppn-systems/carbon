// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Runtime.CompilerServices;

namespace Carbon.Testing;

/// <summary>
/// The one way every zero-allocation test measures managed allocations on its thread (audit round 3, "Flaky guard"):
/// two zero-allocation tests in two assemblies each failed once without a byte count. A result reports the bytes, a
/// repeat window, the iterations, the elapsed time, the GC collections and the JIT compilations on the thread during the
/// window, so a one-off runtime charge shows up as evidence in the failure message instead of a bare number.
/// </summary>
internal static class AllocationProbe
{
    /// <summary>
    /// Runs <paramref name="body"/> <paramref name="warmupIterations"/> times so its code reaches steady state (tier-up,
    /// on-stack replacement, lazy statics, pool growth), then measures <paramref name="iterations"/> calls twice. Assert on
    /// <see cref="AllocationResult.Bytes"/> (the first window); <see cref="AllocationResult.RepeatBytes"/> is evidence only.
    /// </summary>
    /// <param name="iterations">Calls per measured window.</param>
    /// <param name="warmupIterations">Calls before measuring.</param>
    /// <param name="body">The measured work; receives the call index within its run.</param>
    /// <returns>The first window, with the repeat window's bytes.</returns>
    public static AllocationResult Measure(int iterations, int warmupIterations, Action<int> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        Run(body, warmupIterations);
        AllocationWindow first = Start();
        Run(body, iterations);
        AllocationResult result = first.Stop(iterations);
        AllocationWindow repeat = Start();
        Run(body, iterations);
        return result with { RepeatBytes = repeat.Stop(iterations).Bytes };
    }

    /// <summary>Starts a window around code that cannot be repeated (one-shot work on state); warm it up first.</summary>
    /// <returns>The open window.</returns>
    public static AllocationWindow Start() => new(
        Stopwatch.GetTimestamp(),
        GC.CollectionCount(0),
        GC.CollectionCount(1),
        GC.CollectionCount(2),
        JitInfo.GetCompiledMethodCount(currentThread: true),
        Environment.CurrentManagedThreadId,
        GC.GetAllocatedBytesForCurrentThread());

    // Fully optimized at first call, so the probe's own loop never tiers up or goes through on-stack replacement
    // inside a measured window.
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
    private static void Run(Action<int> body, int iterations)
    {
        for (int i = 0; i < iterations; i++)
        {
            body(i);
        }
    }
}

/// <summary>An open allocation window (see <see cref="AllocationProbe.Start"/>).</summary>
internal readonly struct AllocationWindow
{
    private readonly long _timestamp;
    private readonly int _gen0;
    private readonly int _gen1;
    private readonly int _gen2;
    private readonly long _jit;
    private readonly int _threadId;
    private readonly long _bytes;

    internal AllocationWindow(long timestamp, int gen0, int gen1, int gen2, long jit, int threadId, long bytes)
    {
        _timestamp = timestamp;
        _gen0 = gen0;
        _gen1 = gen1;
        _gen2 = gen2;
        _jit = jit;
        _threadId = threadId;
        _bytes = bytes;
    }

    /// <summary>Closes the window. Reads the allocation counter first, so nothing here is counted.</summary>
    /// <param name="iterations">How many calls the window measured.</param>
    /// <returns>What the window saw.</returns>
    public AllocationResult Stop(int iterations)
    {
        long bytes = GC.GetAllocatedBytesForCurrentThread() - _bytes;
        TimeSpan elapsed = Stopwatch.GetElapsedTime(_timestamp);
        return new AllocationResult(
            bytes,
            RepeatBytes: null,
            iterations,
            elapsed,
            GC.CollectionCount(0) - _gen0,
            GC.CollectionCount(1) - _gen1,
            GC.CollectionCount(2) - _gen2,
            JitInfo.GetCompiledMethodCount(currentThread: true) - _jit,
            _threadId,
            Environment.CurrentManagedThreadId == _threadId);
    }
}

/// <summary>What one allocation window saw; its <see cref="ToString"/> is the evidence for an assertion message.</summary>
/// <param name="Bytes">Managed bytes allocated on the thread during the window.</param>
/// <param name="RepeatBytes">Bytes of an identical second window (<see cref="AllocationProbe.Measure"/> only).</param>
/// <param name="Iterations">Calls in the window.</param>
/// <param name="Elapsed">Wall time of the window.</param>
/// <param name="Gen0Collections">Gen-0 collections during the window (any thread).</param>
/// <param name="Gen1Collections">Gen-1 collections during the window.</param>
/// <param name="Gen2Collections">Gen-2 collections during the window.</param>
/// <param name="JitCompilations">Methods JIT-compiled on this thread during the window.</param>
/// <param name="ThreadId">The managed thread the window was opened on.</param>
/// <param name="SameThread">Whether it was also closed on that thread (a measurement across threads means nothing).</param>
internal readonly record struct AllocationResult(
    long Bytes,
    long? RepeatBytes,
    int Iterations,
    TimeSpan Elapsed,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    long JitCompilations,
    int ThreadId,
    bool SameThread)
{
    /// <summary>Gets the mean bytes per call.</summary>
    public double BytesPerIteration => this.Iterations > 0 ? (double)this.Bytes / this.Iterations : this.Bytes;

    /// <inheritdoc/>
    public override string ToString()
    {
        string repeat = this.RepeatBytes is long bytes ? string.Create(CultureInfo.InvariantCulture, $" (repeat window {bytes} B)") : string.Empty;
        string thread = this.SameThread ? string.Empty : " (closed on another thread)";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{this.Bytes} B over {this.Iterations} iterations in {this.Elapsed.TotalMilliseconds:F1} ms{repeat}; GCs gen0/1/2 +{this.Gen0Collections}/+{this.Gen1Collections}/+{this.Gen2Collections}; JIT compilations on the thread +{this.JitCompilations}; thread {this.ThreadId}{thread}");
    }
}
