// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.UI.Controls;
using FluentAssertions;
using Xunit;

namespace Carbon.Graphics.UI.Tests;

public sealed class ProgressBarTests
{
    [Fact]
    public void Progress_ShouldCalculateAndClampPercentageCorrectly()
    {
        ProgressBar bar = new(new RectangleF(0f, 0f, 200f, 20f), value: 50f, maxValue: 100f);
        bar.Progress.Should().Be(0.5f);

        // Clamped above max
        bar.Value = 150f;
        bar.Progress.Should().Be(1.0f);

        // Clamped below zero
        bar.Value = -10f;
        bar.Progress.Should().Be(0.0f);

        // MaxValue zero
        bar.MaxValue = 0f;
        bar.Progress.Should().Be(0.0f);
    }

    [Fact]
    public void Update_GhostTrail_ShouldInterpolateTowardsCurrentProgress()
    {
        ProgressBar bar = new(new RectangleF(0f, 0f, 200f, 20f), value: 100f, maxValue: 100f)
        {
            GhostSpeed = 1.0f // 1.0 units per second
        };

        bar.Progress.Should().Be(1.0f);
        bar.GhostProgress.Should().Be(1.0f);

        // Take damage: HP drops to 50%
        bar.Value = 50f;
        bar.Progress.Should().Be(0.5f);
        bar.GhostProgress.Should().Be(1.0f); // Hasn't updated yet

        // Tick 0.2 seconds -> ghost drops by 0.2 (from 1.0 to 0.8)
        bar.Update(0.2f);
        bar.GhostProgress.Should().BeApproximately(0.8f, 0.001f);

        // Tick 0.5 seconds -> ghost reaches target and clamps at 0.5f
        bar.Update(0.5f);
        bar.GhostProgress.Should().Be(0.5f);
    }
}
