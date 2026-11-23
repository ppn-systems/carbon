// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Graphics.Windowing.Input;
using FluentAssertions;
using Silk.NET.Input;
using Xunit;

namespace Carbon.Graphics.Windowing.Tests;

public sealed class MouseSnapshotTests
{
    [Fact]
    public void Delta_ComputesDifferenceBetweenCurrentAndPreviousPosition()
    {
        // Arrange
        Vector2 current = new(150f, 220f);
        Vector2 previous = new(100f, 200f);
        MouseSnapshot snapshot = new(current, previous, Vector2.Zero, [], []);

        // Act & Assert
        snapshot.Delta.X.Should().Be(50f);
        snapshot.Delta.Y.Should().Be(20f);
    }

    [Fact]
    public void IsButtonPressed_WhenButtonNewlyDown_ReturnsTrue()
    {
        // Arrange
        bool[] current = new bool[16];
        bool[] previous = new bool[16];
        current[(int)MouseButton.Left] = true;
        previous[(int)MouseButton.Left] = false;

        MouseSnapshot snapshot = new(Vector2.Zero, Vector2.Zero, Vector2.Zero, current, previous);

        // Act & Assert
        snapshot.IsButtonDown(MouseButton.Left).Should().BeTrue();
        snapshot.IsButtonPressed(MouseButton.Left).Should().BeTrue();
        snapshot.IsButtonReleased(MouseButton.Left).Should().BeFalse();
    }
}
