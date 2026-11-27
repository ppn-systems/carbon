// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using Carbon.Desktop.Rendering.Hud;
using Carbon.Testing;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>Audit round 2, PERF-7: HUD numbers formatted into spans read exactly like the strings they replace.</summary>
public sealed class HudNumberFormattingTests
{
    [Theory]
    [InlineData(0L)]
    [InlineData(7L)]
    [InlineData(1_234L)]
    [InlineData(50_000L)]
    [InlineData(-9_876_543L)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void WalletFormat_AnyBalance_MatchesInvariantN0(long value)
    {
        Span<char> buffer = stackalloc char[32];

        string formatted = new(WalletWidget.Format(value, buffer));

        formatted.Should().Be(value.ToString("N0", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void WalletFormat_SteadyState_AllocatesNothing()
    {
        AllocationResult window = AllocationProbe.Measure(1_000, warmupIterations: 1_000, i =>
        {
            Span<char> buffer = stackalloc char[32];
            _ = WalletWidget.Format(1_234_567L + i, buffer);
        });

        window.Bytes.Should().Be(0, $"the wallet is drawn every frame ({window})");
    }
}
