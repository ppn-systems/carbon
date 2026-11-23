// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.UI.Common;
using Carbon.Graphics.UI.Controls;
using FluentAssertions;
using Xunit;

namespace Carbon.Graphics.UI.Tests;

public sealed class ButtonTests
{
    [Fact]
    public void Update_MouseHover_ShouldChangeStateToHovered()
    {
        Button button = new(new RectangleF(10f, 10f, 100f, 40f), "Click Me");

        button.State.Should().Be(ControlState.Normal);

        // Move mouse inside button bounds
        button.Update(new Vector2(50f, 25f), isMouseDown: false, wasMouseDown: false);

        button.State.Should().Be(ControlState.Hovered);

        // Move mouse outside button bounds
        button.Update(new Vector2(200f, 200f), isMouseDown: false, wasMouseDown: false);

        button.State.Should().Be(ControlState.Normal);
    }

    [Fact]
    public void Update_MouseClickSequence_ShouldTriggerClickedEvent()
    {
        Button button = new(new RectangleF(0f, 0f, 100f, 40f), "Submit");
        bool clicked = false;
        button.Clicked += b => clicked = true;

        Vector2 inside = new(50f, 20f);

        // Step 1: Mouse down inside
        button.Update(inside, isMouseDown: true, wasMouseDown: false);
        button.State.Should().Be(ControlState.Pressed);
        clicked.Should().BeFalse();

        // Step 2: Mouse released inside
        button.Update(inside, isMouseDown: false, wasMouseDown: true);
        button.State.Should().Be(ControlState.Hovered);
        clicked.Should().BeTrue();
    }

    [Fact]
    public void Update_MouseDownInside_ReleasedOutside_ShouldNotTriggerClick()
    {
        Button button = new(new RectangleF(0f, 0f, 100f, 40f), "Cancel");
        bool clicked = false;
        button.Clicked += b => clicked = true;

        // Mouse down inside
        button.Update(new Vector2(50f, 20f), isMouseDown: true, wasMouseDown: false);
        button.State.Should().Be(ControlState.Pressed);

        // Mouse moved outside while still holding button
        button.Update(new Vector2(200f, 200f), isMouseDown: true, wasMouseDown: true);

        // Mouse released outside
        button.Update(new Vector2(200f, 200f), isMouseDown: false, wasMouseDown: true);

        clicked.Should().BeFalse();
        button.State.Should().Be(ControlState.Normal);
    }

    [Fact]
    public void Update_WhenDisabled_ShouldStayInDisabledState()
    {
        Button button = new(new RectangleF(0f, 0f, 100f, 40f), "Disabled")
        {
            IsEnabled = false
        };

        button.IsFocusable.Should().BeFalse();

        bool clicked = false;
        button.Clicked += b => clicked = true;

        button.Update(new Vector2(50f, 20f), isMouseDown: true, wasMouseDown: false);

        button.State.Should().Be(ControlState.Disabled);
        clicked.Should().BeFalse();
    }
}
