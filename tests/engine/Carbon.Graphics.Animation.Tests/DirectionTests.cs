// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Graphics.Animation.Directions;
using FluentAssertions;
using Xunit;

namespace Carbon.Graphics.Animation.Tests;

public sealed class DirectionTests
{
    [Theory]
    [InlineData(Direction8.North, 0f, -1f)]
    [InlineData(Direction8.South, 0f, 1f)]
    [InlineData(Direction8.East, 1f, 0f)]
    [InlineData(Direction8.West, -1f, 0f)]
    public void Direction8_ToVector_ReturnsExpectedCardinalHeading(Direction8 dir, float expectedX, float expectedY)
    {
        // Act
        Vector2 heading = dir.ToVector();

        // Assert
        heading.X.Should().BeApproximately(expectedX, 0.001f);
        heading.Y.Should().BeApproximately(expectedY, 0.001f);
    }

    [Theory]
    [InlineData(10f, 0f, Direction8.East)]
    [InlineData(-10f, 0f, Direction8.West)]
    [InlineData(0f, 10f, Direction8.South)]
    [InlineData(0f, -10f, Direction8.North)]
    public void DirectionExtensions_FromVector_MapsVectorToClosestDirection8(float vx, float vy, Direction8 expected)
    {
        // Act
        Direction8 result = DirectionExtensions.FromVector(new Vector2(vx, vy));

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void Direction8_ToDirection4_PreservesCardinalHeading()
    {
        // Act & Assert
        Direction8.North.ToDirection4().Should().Be(Direction4.North);
        Direction8.South.ToDirection4().Should().Be(Direction4.South);
        Direction8.East.ToDirection4().Should().Be(Direction4.East);
        Direction8.West.ToDirection4().Should().Be(Direction4.West);
    }
}
