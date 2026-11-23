// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Simulation.Clock;

/// <summary>
/// Encapsulates contextual simulation step data for a fixed tick.
/// </summary>
/// <param name="TickIndex">Monotonically increasing tick index since simulation start.</param>
/// <param name="DeltaTime">Fixed delta time duration of this tick in seconds.</param>
/// <param name="TotalTime">Accumulated simulation time in seconds.</param>
public readonly record struct FixedTickArgs(ulong TickIndex, float DeltaTime, double TotalTime);
