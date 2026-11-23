// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Scenes.Transitions;
using FluentAssertions;
using Xunit;

namespace Carbon.Graphics.Scenes.Tests;

public sealed class SceneTransitionTests
{
    [Fact]
    public void Start_TwoPhaseTransition_ExecutesMidpointSwapWhenScreenObscured()
    {
        // Arrange
        SceneTransition transition = new();
        bool midpointExecuted = false;

        transition.Start(
            SceneTransitionEffect.Fade,
            duration: 1.0f,
            ColorRgba.Black,
            onMidpointSwap: () => midpointExecuted = true);

        // Act & Assert
        transition.IsActive.Should().BeTrue();
        transition.Alpha.Should().Be(0f);

        // 1. Advance halfway through FadeOut (0.25s / 0.5s half-duration)
        transition.Update(0.25);
        transition.Alpha.Should().BeGreaterThan(0f).And.BeLessThan(1f);
        midpointExecuted.Should().BeFalse();

        // 2. Advance to midpoint (0.25s more -> 0.5s reached)
        transition.Update(0.25);
        midpointExecuted.Should().BeTrue("midpoint callback must fire when screen is fully obscured");
        transition.Alpha.Should().Be(1f);

        // 3. Advance through FadeIn to completion
        transition.Update(0.5);
        transition.IsActive.Should().BeFalse();
        transition.Alpha.Should().Be(0f);
    }
}
