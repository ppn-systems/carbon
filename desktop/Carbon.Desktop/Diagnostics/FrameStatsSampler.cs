// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics;
using System.Globalization;

namespace Carbon.Desktop.Diagnostics;

/// <summary>
/// Opt-in frame statistics (<c>CARBON_FRAME_STATS=n</c>, audit round 2 PERF-11). For every window of frames it logs
/// CPU time of update + render, the wall-clock interval between frames (which includes the buffer swap, so it also
/// counts the GPU's fill work under a software rasterizer), managed bytes allocated on the loop thread, GC count and
/// draw calls, so client performance can be measured headless (tools/screenshots/capture.sh runs the client under xvfb).
/// </summary>
internal sealed class FrameStatsSampler
{
    private readonly Func<long> _drawCalls;
    private readonly int _window;
    private readonly double[] _frameMs;
    private readonly double[] _wallMs;
    private long _lastFrameStart;
    private double _wallInterval;
    private string _scene = string.Empty;
    private long _frameStart;
    private long _allocStart;
    private long _drawStart;
    private int _count;
    private long _allocTotal;
    private long _drawTotal;
    private long _drawMax;
    private int _updateDepth;

    public FrameStatsSampler(Func<long> drawCalls, int window = 120)
    {
        _drawCalls = drawCalls;
        _window = window;
        _frameMs = new double[window];
        _wallMs = new double[window];
    }

    /// <summary>Marks the start of a frame (before update). Calls nest safely if update runs several times per render.</summary>
    public void BeginFrame()
    {
        if (_updateDepth++ > 0)
        {
            return;
        }

        long now = Stopwatch.GetTimestamp();
        _wallInterval = _lastFrameStart == 0 ? 0d : Stopwatch.GetElapsedTime(_lastFrameStart, now).TotalMilliseconds;
        _lastFrameStart = now;
        _frameStart = now;
        _allocStart = GC.GetAllocatedBytesForCurrentThread();
        _drawStart = _drawCalls();
    }

    /// <summary>Marks the end of a frame (after render) for <paramref name="scene"/>.</summary>
    public void EndFrame(string scene)
    {
        if (_updateDepth == 0)
        {
            return;
        }

        _updateDepth = 0;
        double ms = Stopwatch.GetElapsedTime(_frameStart).TotalMilliseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - _allocStart;
        long draws = _drawCalls() - _drawStart;

        if (!string.Equals(scene, _scene, StringComparison.Ordinal))
        {
            _scene = scene;
            this.Reset();
        }

        _wallMs[_count] = _wallInterval;
        _frameMs[_count++] = ms;
        _allocTotal += allocated;
        _drawTotal += draws;
        _drawMax = Math.Max(_drawMax, draws);

        if (_count == _window)
        {
            Array.Sort(_frameMs);
            Array.Sort(_wallMs);
            double sum = 0;
            for (int i = 0; i < _window; i++)
            {
                sum += _frameMs[i];
            }

            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[FrameStats] scene={_scene} frames={_window} cpu_ms avg={sum / _window:F2} p50={_frameMs[_window / 2]:F2} p95={_frameMs[(int)(_window * 0.95)]:F2} max={_frameMs[^1]:F2} wall_ms p50={_wallMs[_window / 2]:F2} p95={_wallMs[(int)(_window * 0.95)]:F2} alloc_B_per_frame={_allocTotal / _window} draws_per_frame avg={(double)_drawTotal / _window:F1} max={_drawMax} gen0_total={GC.CollectionCount(0)}"));
            this.Reset();
        }
    }

    private void Reset()
    {
        _count = 0;
        _allocTotal = 0;
        _drawTotal = 0;
        _drawMax = 0;
    }
}
