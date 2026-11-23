// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.UI.Common;
using Carbon.Graphics.UI.Layout;
using FluentAssertions;
using Xunit;

namespace Carbon.Graphics.UI.Tests;

public sealed class LayoutEngineTests
{
    [Fact]
    public void CalculateGridSlotBounds_ShouldPositionCellsCorrectly()
    {
        RectangleF container = new(100f, 100f, 400f, 300f);
        Padding padding = new(10f, 20f, 10f, 20f);
        float slotW = 40f;
        float slotH = 40f;
        float spacing = 5f;

        // Cell (0, 0)
        RectangleF cell00 = LayoutEngine.CalculateGridSlotBounds(container, slotW, slotH, spacing, padding, col: 0, row: 0);
        cell00.X.Should().Be(110f); // 100 + left 10
        cell00.Y.Should().Be(120f); // 100 + top 20
        cell00.Width.Should().Be(40f);
        cell00.Height.Should().Be(40f);

        // Cell (2, 1)
        RectangleF cell21 = LayoutEngine.CalculateGridSlotBounds(container, slotW, slotH, spacing, padding, col: 2, row: 1);
        cell21.X.Should().Be(110f + (2 * 45f)); // 200
        cell21.Y.Should().Be(120f + (1 * 45f)); // 165
    }

    [Fact]
    public void CalculateCenteredRowItemBounds_ShouldCenterItemsCorrectly()
    {
        RectangleF container = new(0f, 0f, 200f, 50f);
        float itemW = 50f;
        float itemH = 30f;
        int count = 2;
        float spacing = 10f;
        // Total row width = 2 * 50 + 10 = 110. Centered start X = (200 - 110) / 2 = 45. Y = (50 - 30) / 2 = 10.

        RectangleF item0 = LayoutEngine.CalculateCenteredRowItemBounds(container, itemW, itemH, count, index: 0, spacing);
        item0.X.Should().Be(45f);
        item0.Y.Should().Be(10f);

        RectangleF item1 = LayoutEngine.CalculateCenteredRowItemBounds(container, itemW, itemH, count, index: 1, spacing);
        item1.X.Should().Be(45f + 50f + 10f); // 105
        item1.Y.Should().Be(10f);
    }
}
