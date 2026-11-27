// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Desktop.Rendering.Hud;
using Carbon.Graphics.Batching.Common;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>
/// The top-right HUD stack: the minimap flush at the corner, the clock under it, the status-effect strip under the clock
/// (wrapping rows as wide as the column), then toasts and the skill bar under the last strip row.
/// </summary>
public sealed class HudLayoutTests
{
    [Theory]
    [InlineData(1024)]
    [InlineData(1280)]
    [InlineData(1920)]
    [InlineData(2560)]
    public void GetMinimapFrameRect_AnyWidth_SitsFlushAtTheTopRightWithTheClockUnderIt(int width)
    {
        RectangleF frame = HudLayout.GetMinimapFrameRect(width);
        RectangleF map = HudLayout.GetMinimapMapRect(width);
        RectangleF clock = HudLayout.GetClockRect(width, showMinimap: true);

        frame.Y.Should().Be(HudLayout.TopMargin, "nothing is reserved above the minimap any more");
        frame.Right.Should().Be(width - HudLayout.EdgeMargin);
        (map.X >= frame.X && map.Right <= frame.Right && map.Y >= frame.Y && map.Bottom <= frame.Bottom).Should().BeTrue("the map sits inside its frame");
        clock.Y.Should().Be(frame.Bottom + HudLayout.Gap, "the clock stacks right under the minimap");
        clock.Right.Should().Be(frame.Right);
    }

    [Theory]
    [InlineData(1280, true)]
    [InlineData(1280, false)]
    [InlineData(1920, true)]
    [InlineData(1024, false)]
    public void GetStatusStripRect_UnderTheColumn_NoWiderThanTheMinimapAndRightAligned(int width, bool showMinimap)
    {
        RectangleF strip = HudLayout.GetStatusStripRect(width, showMinimap, rows: 1);
        RectangleF minimap = HudLayout.GetMinimapFrameRect(width);
        RectangleF clock = HudLayout.GetClockRect(width, showMinimap);

        strip.Y.Should().BeGreaterThan(clock.Bottom, "the strip sits under the clock");
        if (showMinimap)
        {
            strip.Y.Should().BeGreaterThan(minimap.Bottom, "the status effects show below the map");
        }

        strip.Width.Should().BeLessThanOrEqualTo(minimap.Width, "the strip is as wide as the minimap at most");
        strip.Right.Should().Be(minimap.Right, "the strip is right-aligned with the minimap");
    }

    [Theory]
    [InlineData(1280)]
    [InlineData(1920)]
    public void GetClockRect_MinimapHidden_MovesTheClockToTheCornerAndTheStripUnderIt(int width)
    {
        RectangleF clock = HudLayout.GetClockRect(width, showMinimap: false);
        RectangleF strip = HudLayout.GetStatusStripRect(width, showMinimap: false, rows: 1);

        clock.Y.Should().Be(HudLayout.TopMargin, "without the minimap the clock moves up to the corner");
        strip.Y.Should().Be(clock.Bottom + HudLayout.Gap, "and the strip moves up under the clock");
        strip.Y.Should().BeLessThan(HudLayout.GetStatusStripRect(width, showMinimap: true, rows: 1).Y);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(6, 2)]
    [InlineData(9, 3)]
    public void Icon_1_6_9Effects_WrapIntoRowsInsideTheStripWidthWithoutOverlapping(int count, int expectedRows)
    {
        const int Width = 1280;
        StatusEffectStripLayout.IconsPerRow(Width).Should().Be(4, "four 34 px icons with 6 px gaps fit the 196 px column");
        int rows = StatusEffectStripLayout.Rows(count, Width);
        rows.Should().Be(expectedRows);
        RectangleF strip = HudLayout.GetStatusStripRect(Width, showMinimap: true, rows);
        RectangleF minimap = HudLayout.GetMinimapFrameRect(Width);
        RectangleF clock = HudLayout.GetClockRect(Width, showMinimap: true);

        RectangleF[] icons = new RectangleF[count];
        for (int i = 0; i < count; i++)
        {
            icons[i] = StatusEffectStripLayout.Icon(i, Width, showMinimap: true);
            Contains(strip, icons[i]).Should().BeTrue($"icon {i} stays inside the {rows}-row strip");
            icons[i].Intersects(minimap).Should().BeFalse($"icon {i} must not cover the minimap");
            icons[i].Intersects(clock).Should().BeFalse($"icon {i} must not cover the clock");
            for (int j = 0; j < i; j++)
            {
                icons[i].Intersects(icons[j]).Should().BeFalse($"icons {j} and {i} overlap");
            }
        }

        icons[0].Right.Should().Be(strip.Right - 3f, "the first effect sits at the strip's right edge");
        icons[count - 1].Y.Should().Be(strip.Y + ((expectedRows - 1) * HudLayout.StatusRowHeight) + 3f, "the last effect is on the last row");
        if (count > 4)
        {
            icons[4].Right.Should().Be(icons[0].Right, "the fifth icon starts the second row at the right edge");
            icons[4].Y.Should().Be(icons[0].Y + HudLayout.StatusRowHeight);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(9)]
    public void GetToastTopAndSkillBarMinTop_StartBelowTheLastStripRow(int count)
    {
        const int Width = 1280;
        foreach (bool showMinimap in new[] { true, false })
        {
            int rows = StatusEffectStripLayout.Rows(count, Width);
            float columnBottom = HudLayout.GetClockRect(Width, showMinimap).Bottom;
            float lastIconBottom = columnBottom;
            for (int i = 0; i < count; i++)
            {
                lastIconBottom = System.MathF.Max(lastIconBottom, StatusEffectStripLayout.Icon(i, Width, showMinimap).Bottom);
            }

            float stripBottom = rows > 0 ? HudLayout.GetStatusStripRect(Width, showMinimap, rows).Bottom : columnBottom;
            HudLayout.GetToastTop(Width, showMinimap, rows).Should().BeGreaterThan(lastIconBottom, "toasts start under the last strip row");
            HudLayout.GetToastTop(Width, showMinimap, rows).Should().BeGreaterThanOrEqualTo(stripBottom);
            HudLayout.GetSkillBarMinTop(Width, showMinimap, rows).Should().BeGreaterThanOrEqualTo(stripBottom, "the skill bar never covers the strip");
            if (showMinimap)
            {
                HudLayout.GetToastTop(Width, showMinimap, rows).Should().BeGreaterThan(HudLayout.GetMinimapFrameRect(Width).Bottom);
            }
        }
    }

    [Fact]
    public void GetToastTop_MoreRows_MovesToastsDownOneRowAtATime()
    {
        float one = HudLayout.GetToastTop(1280, showMinimap: true, statusRows: 1);
        float three = HudLayout.GetToastTop(1280, showMinimap: true, statusRows: 3);

        (three - one).Should().Be(2f * HudLayout.StatusRowHeight, "toasts follow the actual number of rows");
    }

    private static bool Contains(RectangleF outer, RectangleF inner) =>
        inner.X >= outer.X && inner.Y >= outer.Y && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;
}
