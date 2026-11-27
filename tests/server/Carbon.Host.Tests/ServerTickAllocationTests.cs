// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Carbon.Host.Tests;

/// <summary>
/// AGENTS.md rule 4: the steady-state world tick must not allocate. Eight moving players and the monsters that
/// populate around them are stepped through a real <c>AuthoritativeWorld</c> and the managed bytes allocated on the
/// tick thread are measured (audit round 2, PERF-1).
/// </summary>
public sealed class ServerTickAllocationTests(ITestOutputHelper output)
{
    /// <summary>Ticks run first so chunk streaming, pools and collection capacities reach steady state.</summary>
    private const int WarmupTicks = 900;

    private const int MeasuredTicks = 900;

    /// <summary>
    /// Allowed average bytes per tick. Measured at 136 before PERF-1 and about 2 after. The remainder is monster
    /// respawn (stat sheets, attachment stores; audit ECS-3). Lower this as allocations are removed; never raise it.
    /// </summary>
    internal const double MaxBytesPerTick = 32;

    [Fact]
    public void StepFixedTick_SteadyStateWithPlayersAndMonsters_StaysWithinAllocationBudget()
    {
        using TickAllocationHarness harness = TickAllocationHarness.Create(players: 8);
        for (int i = 0; i < WarmupTicks; i++)
        {
            harness.Step();
        }

        (double bytesPerTick, double millisecondsPerTick) = harness.Measure(MeasuredTicks);
        output.WriteLine($"8 players: {bytesPerTick:F1} B/tick, {millisecondsPerTick:F3} ms/tick");

        bytesPerTick.Should().BeLessThanOrEqualTo(
            MaxBytesPerTick,
            "the fixed tick is a hot path (AGENTS.md rule 4): no LINQ, closures, boxing or per-tick arrays");
    }
}
