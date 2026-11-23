// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.UI.NineSlice;
using FluentAssertions;
using Xunit;

namespace Carbon.Graphics.UI.Tests;

public sealed class NineSliceGridTests
{
    [Fact]
    public void Create_ComputesNineDistinctRegionsCorrectly()
    {
        // Arrange
        RectangleF source = new(0f, 0f, 30f, 30f);
        NineSliceMargins margins = new(10f, 10f, 10f, 10f);
        RectangleF dest = new(100f, 100f, 100f, 80f);

        // Act
        NineSliceGrid grid = NineSliceGrid.Create(source, margins, dest);

        // Assert: Corners preserve margin dimensions (10x10)
        grid.DestTopLeft.Width.Should().Be(10f);
        grid.DestTopLeft.Height.Should().Be(10f);
        grid.DestTopLeft.X.Should().Be(100f);
        grid.DestTopLeft.Y.Should().Be(100f);

        // TopRight corner
        grid.DestTopRight.X.Should().Be(190f);
        grid.DestTopRight.Y.Should().Be(100f);

        // Center stretch area: width = 100 - 10 - 10 = 80; height = 80 - 10 - 10 = 60
        grid.DestCenter.Width.Should().Be(80f);
        grid.DestCenter.Height.Should().Be(60f);
    }
}
