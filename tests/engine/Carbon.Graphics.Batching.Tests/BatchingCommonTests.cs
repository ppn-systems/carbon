// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Sprites;
using FluentAssertions;
using Xunit;

namespace Carbon.Graphics.Batching.Tests;

public sealed class BatchingCommonTests
{
    [Fact]
    public void ColorRgba_PackedValue_MatchesLittleEndianRgbaLayout()
    {
        // Arrange
        ColorRgba color = new(0x12, 0x34, 0x56, 0x78);

        // Act & Assert
        // 0x78563412
        color.PackedValue.Should().Be(0x78563412);
    }

    [Fact]
    public void ColorRgba_MultiplyOperator_ModulatesComponentWise()
    {
        // Arrange
        ColorRgba white = ColorRgba.White;
        ColorRgba halfRed = new(128, 0, 0, 255);

        // Act
        ColorRgba result = white * halfRed;

        // Assert
        result.R.Should().Be(128);
        result.G.Should().Be(0);
        result.B.Should().Be(0);
        result.A.Should().Be(255);
    }

    [Fact]
    public void RectangleF_Intersects_WhenOverlapping_ReturnsTrue()
    {
        // Arrange
        RectangleF a = new(0f, 0f, 100f, 100f);
        RectangleF b = new(50f, 50f, 100f, 100f);
        RectangleF c = new(200f, 200f, 10f, 10f);

        // Act & Assert
        a.Intersects(b).Should().BeTrue();
        a.Intersects(c).Should().BeFalse();
    }

    [Fact]
    public void SpriteSortKey_Calculate_ForImmediate_ReturnsZeroWithoutThrowing()
    {
        // Act
        float key = SpriteSortKey.Calculate(DepthSortMode.Immediate, footY: 150f, layer: 2, depth: 0.5f, textureHandle: 10);

        // Assert
        key.Should().Be(0f);
    }

    [Fact]
    public void SpriteSortKey_Calculate_ForYSorted_OrdersByLayerThenFootY()
    {
        // Act
        float lowerY = SpriteSortKey.Calculate(DepthSortMode.YSorted, footY: 100f, layer: 0, depth: 0f, textureHandle: 1);
        float higherY = SpriteSortKey.Calculate(DepthSortMode.YSorted, footY: 200f, layer: 0, depth: 0f, textureHandle: 1);
        float higherLayer = SpriteSortKey.Calculate(DepthSortMode.YSorted, footY: 100f, layer: 1, depth: 0f, textureHandle: 1);

        // Assert
        higherY.Should().BeGreaterThan(lowerY);
        higherLayer.Should().BeGreaterThan(higherY);
    }
}
