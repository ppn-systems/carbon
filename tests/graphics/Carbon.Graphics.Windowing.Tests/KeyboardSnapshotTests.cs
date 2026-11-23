// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Graphics.Windowing.Input;
using FluentAssertions;
using Silk.NET.Input;
using Xunit;

namespace Carbon.Graphics.Windowing.Tests;

public sealed class KeyboardSnapshotTests
{
    [Fact]
    public void IsKeyPressed_WhenKeyTransitionedFromUpToDown_ReturnsTrue()
    {
        // Arrange
        bool[] current = new bool[512];
        bool[] previous = new bool[512];
        current[(int)Key.Space] = true;
        previous[(int)Key.Space] = false;

        KeyboardSnapshot snapshot = new(current, previous);

        // Act & Assert
        snapshot.IsKeyDown(Key.Space).Should().BeTrue();
        snapshot.IsKeyPressed(Key.Space).Should().BeTrue();
        snapshot.IsKeyReleased(Key.Space).Should().BeFalse();
        snapshot.IsKeyUp(Key.Space).Should().BeFalse();
    }

    [Fact]
    public void IsKeyReleased_WhenKeyTransitionedFromDownToUp_ReturnsTrue()
    {
        // Arrange
        bool[] current = new bool[512];
        bool[] previous = new bool[512];
        current[(int)Key.Enter] = false;
        previous[(int)Key.Enter] = true;

        KeyboardSnapshot snapshot = new(current, previous);

        // Act & Assert
        snapshot.IsKeyDown(Key.Enter).Should().BeFalse();
        snapshot.IsKeyPressed(Key.Enter).Should().BeFalse();
        snapshot.IsKeyReleased(Key.Enter).Should().BeTrue();
        snapshot.IsKeyUp(Key.Enter).Should().BeTrue();
    }
}
