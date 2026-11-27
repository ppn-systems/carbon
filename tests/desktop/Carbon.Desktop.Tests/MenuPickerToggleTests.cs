// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Desktop.Menu;
using Carbon.Graphics.Batching.Common;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>The creator's reusable option row and two-state toggle (docs/art/menu-ui.md rule 18).</summary>
public sealed class MenuPickerToggleTests
{
    private static readonly RectangleF s_row = new(100f, 100f, 240f, 28f);

    [Fact]
    public void MenuPickerStep_WrapsBothWays()
    {
        MenuPicker picker = new(4);

        picker.Step(-1).Should().BeTrue();
        picker.Index.Should().Be(3, "left from the first choice wraps to the last");
        picker.Step(1).Should().BeTrue();
        picker.Index.Should().Be(0);
    }

    [Fact]
    public void MenuPickerUpdate_ClickingTheArrowsSteps()
    {
        MenuPicker picker = new(4) { Bounds = s_row };
        Vector2 next = new(picker.NextBounds.X + 4f, picker.NextBounds.Y + 4f);
        Vector2 prev = new(picker.PrevBounds.X + 4f, picker.PrevBounds.Y + 4f);
        Vector2 middle = new(s_row.X + (s_row.Width * 0.5f), s_row.Y + 10f);

        picker.Update(next, leftDown: true, leftPressed: true, dt: 0f).Should().Be(1);
        picker.Update(middle, leftDown: true, leftPressed: true, dt: 0f).Should().Be(0, "only the arrows step");
        picker.Update(prev, leftDown: true, leftPressed: true, dt: 0f).Should().Be(-1);
        picker.Index.Should().Be(0);
    }

    [Fact]
    public void MenuPicker_DisabledOrSingleChoice_DoesNotStep()
    {
        MenuPicker disabled = new(4) { IsEnabled = false };
        disabled.Step(1).Should().BeFalse();
        disabled.Index.Should().Be(0);

        MenuPicker single = new(1);
        single.Step(1).Should().BeFalse("one choice has nowhere to go");
    }

    [Fact]
    public void MenuPickerSetCount_ClampsTheIndexWhenChoicesShrink()
    {
        MenuPicker picker = new(6) { Index = 5 };

        picker.SetCount(3);

        picker.Index.Should().Be(2, "a race with fewer options keeps a valid pick");
    }

    [Fact]
    public void MenuToggleUpdate_ClickingTheOtherHalfSelectsIt()
    {
        MenuToggle toggle = new("A", "B") { Bounds = s_row };
        Vector2 right = new(s_row.Right - 10f, s_row.Y + 10f);
        Vector2 left = new(s_row.X + 10f, s_row.Y + 10f);

        toggle.Update(left, leftPressed: true, dt: 0f).Should().BeFalse("the chosen half is already selected");
        toggle.Update(right, leftPressed: true, dt: 0f).Should().BeTrue();
        toggle.Selected.Should().Be(1);
        toggle.Hovered.Should().Be(1);
    }

    [Fact]
    public void MenuToggleFlip_AlternatesUnlessDisabled()
    {
        MenuToggle toggle = new("A", "B");
        toggle.Flip().Should().BeTrue();
        toggle.Selected.Should().Be(1);
        toggle.Flip();
        toggle.Selected.Should().Be(0);

        toggle.IsEnabled = false;
        toggle.Flip().Should().BeFalse();
        toggle.Selected.Should().Be(0);
    }
}
