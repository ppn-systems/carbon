// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Simulation.Clock;
using FluentAssertions;
using Xunit;

namespace Carbon.Simulation.Tests;

public sealed class FixedTickClockTests
{
    [Fact]
    public void Constructor_CalculatesDeltaTimeAndInterval()
    {
        FixedTickClock clock30 = new(30);
        clock30.TickRate.Should().Be(30);
        clock30.DeltaTime.Should().BeApproximately(1f / 30f, 0.0001f);

        FixedTickClock clock60 = new(60);
        clock60.TickRate.Should().Be(60);
        clock60.DeltaTime.Should().BeApproximately(1f / 60f, 0.0001f);
    }

    [Fact]
    public void Advance_AccumulatedTime_FiresExactTicks()
    {
        FixedTickClock clock = new(20); // 1 tick every 0.05 seconds
        int tickCount = 0;

        int executed = clock.Advance(0.04, _ => tickCount++);
        executed.Should().Be(0);
        tickCount.Should().Be(0);

        executed = clock.Advance(0.07, _ => tickCount++);
        executed.Should().Be(2);
        tickCount.Should().Be(2);
        clock.TickCount.Should().Be(2);
        clock.TotalTime.Should().BeApproximately(0.10, 0.0001);
    }

    [Fact]
    public void Advance_MaxSubSteps_PreventsSpiralOfDeath()
    {
        FixedTickClock clock = new(60);
        int ticks = 0;

        int executed = clock.Advance(1.0, _ => ticks++, maxSubSteps: 3);

        executed.Should().Be(3);
        ticks.Should().Be(3);
    }
}
