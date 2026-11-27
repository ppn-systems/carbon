// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Testing;
using FluentAssertions;
using Xunit;

namespace Carbon.Primitives.Tests;

/// <summary>
/// The shared allocation probe every zero-allocation test relies on (tests/Shared/AllocationProbe.cs): if it stopped
/// running the body or stopped counting, every one of those tests would pass without measuring anything.
/// </summary>
public sealed class AllocationProbeTests
{
    [Fact]
    public void Measure_AllocatingBody_CountsEveryCallInBothWindows()
    {
        int calls = 0;
        object? keep = null;

        AllocationResult result = AllocationProbe.Measure(100, warmupIterations: 7, i =>
        {
            calls++;
            keep = new byte[64];
        });

        calls.Should().Be(7 + 100 + 100, "warm-up, then two measured windows");
        result.Iterations.Should().Be(100);
        result.Bytes.Should().BeGreaterThanOrEqualTo(100 * 64, result.ToString());
        result.RepeatBytes.Should().BeGreaterThanOrEqualTo(100 * 64);
        result.BytesPerIteration.Should().BeGreaterThanOrEqualTo(64);
        result.SameThread.Should().BeTrue();
        keep.Should().NotBeNull();
    }

    [Fact]
    public void Measure_PassesTheCallIndexWithinEachRun()
    {
        int indexSum = 0;

        _ = AllocationProbe.Measure(4, warmupIterations: 3, i => indexSum += i);

        indexSum.Should().Be(3 + 6 + 6, "warm-up 0..2, then each window 0..3");
    }

    [Fact]
    public void Measure_NonAllocatingBody_ReportsZero()
    {
        int sum = 0;

        AllocationResult result = AllocationProbe.Measure(1_000, warmupIterations: 1_000, i => sum += i);

        result.Bytes.Should().Be(0, result.ToString());
        result.RepeatBytes.Should().Be(0);
    }

    [Fact]
    public void StartStop_CountsOnlyWhatHappensInside()
    {
        byte[] before = new byte[256];

        AllocationWindow open = AllocationProbe.Start();
        byte[] inside = new byte[128];
        AllocationResult result = open.Stop(1);

        result.Bytes.Should().BeInRange(128, 255, "the array allocated before Start is not counted");
        result.RepeatBytes.Should().BeNull("a one-shot window has no repeat");
        (before.Length + inside.Length).Should().Be(384);
    }

    [Fact]
    public void ToString_CarriesTheEvidence()
    {
        AllocationResult result = new(48, 0, 1_000, TimeSpan.FromMilliseconds(2.5), 1, 0, 0, 2, 17, SameThread: true);

        result.ToString().Should().Be(
            "48 B over 1000 iterations in 2.5 ms (repeat window 0 B); GCs gen0/1/2 +1/+0/+0; JIT compilations on the thread +2; thread 17");
    }
}
