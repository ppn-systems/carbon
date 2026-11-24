// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Simulation.Clock;

/// <summary>
/// High-precision, deterministic fixed-tick simulation clock.
/// Decouples game logic from variable rendering frame rates using a timestep accumulator.
/// Zero heap allocation on tick execution.
/// </summary>
public sealed class FixedTickClock
{
    private const double MaxDeltaTimeCap = 0.25;

    private readonly double _interval;
    private readonly float _deltaTime;
    private double _accumulator;
    private double _totalTime;
    private ulong _tickCount;

    /// <summary>
    /// Gets the target tick frequency in Hertz (ticks per second).
    /// </summary>
    public int TickRate { get; }

    /// <summary>
    /// Gets the fixed delta time interval of a single tick in seconds.
    /// </summary>
    public float DeltaTime => _deltaTime;

    /// <summary>
    /// Gets the total number of simulation ticks executed since start.
    /// </summary>
    public ulong TickCount => _tickCount;

    /// <summary>
    /// Gets the total accumulated simulation time in seconds.
    /// </summary>
    public double TotalTime => _totalTime;

    /// <summary>
    /// Gets the normalized interpolation alpha fraction [0.0 - 1.0] between previous and next tick state.
    /// Essential for rendering interpolation without simulation jitter.
    /// </summary>
    public float InterpolationAlpha => (float)Math.Clamp(_accumulator / _interval, 0.0, 1.0);

    /// <summary>
    /// Initializes a new instance of the <see cref="FixedTickClock"/> class.
    /// </summary>
    /// <param name="tickRate">The simulation frequency in Hertz (e.g. 20 for standard MMORPG, 30, or 60).</param>
    public FixedTickClock(int tickRate = 30)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tickRate, nameof(tickRate));

        this.TickRate = tickRate;
        _deltaTime = 1f / tickRate;
        _interval = 1.0 / tickRate;
    }

    /// <summary>
    /// Advances elapsed wall-clock time and triggers fixed simulation ticks.
    /// </summary>
    /// <param name="elapsedSeconds">Elapsed wall-clock time in seconds since previous frame.</param>
    /// <param name="onTick">Callback executed for each fixed tick step.</param>
    /// <param name="maxSubSteps">Maximum sub-steps allowed per frame to prevent the spiral of death.</param>
    /// <returns>The number of fixed ticks executed during this update.</returns>
    public int Advance(double elapsedSeconds, Action<FixedTickArgs> onTick, int maxSubSteps = 5)
    {
        ArgumentNullException.ThrowIfNull(onTick, nameof(onTick));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSubSteps, nameof(maxSubSteps));

        double clampedElapsed = Math.Min(elapsedSeconds, MaxDeltaTimeCap);
        _accumulator += clampedElapsed;

        int executedTicks = 0;
        while (_accumulator >= _interval && executedTicks < maxSubSteps)
        {
            _tickCount++;
            _totalTime += _interval;
            _accumulator -= _interval;

            FixedTickArgs args = new(_tickCount, _deltaTime, _totalTime);
            onTick(args);
            executedTicks++;
        }

        return executedTicks;
    }

    /// <summary>
    /// Resets clock accumulators and counters to initial state.
    /// </summary>
    public void Reset()
    {
        _accumulator = 0.0;
        _totalTime = 0.0;
        _tickCount = 0;
    }
}
