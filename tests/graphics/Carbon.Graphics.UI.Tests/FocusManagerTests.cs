// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Graphics.UI.Common;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Carbon.Graphics.UI.Tests;

public sealed class FocusManagerTests
{
    [Fact]
    public void Next_WhenMultipleControlsRegistered_CyclesFocusByTabIndex()
    {
        // Arrange
        FocusManager manager = new();
        IFocusable ctrl1 = Substitute.For<IFocusable>();
        ctrl1.IsFocusable.Returns(true);
        ctrl1.TabIndex.Returns(1);

        IFocusable ctrl2 = Substitute.For<IFocusable>();
        ctrl2.IsFocusable.Returns(true);
        ctrl2.TabIndex.Returns(2);

        manager.Register(ctrl1);
        manager.Register(ctrl2);

        // Act & Assert
        manager.Focus(ctrl1);
        manager.FocusedControl.Should().Be(ctrl1);

        manager.Next();
        manager.FocusedControl.Should().Be(ctrl2);

        manager.Next(); // Wraps around
        manager.FocusedControl.Should().Be(ctrl1);
    }

    [Fact]
    public void Previous_WhenFocusAtStart_WrapsAroundToLastControl()
    {
        // Arrange
        FocusManager manager = new();
        IFocusable ctrl1 = Substitute.For<IFocusable>();
        ctrl1.IsFocusable.Returns(true);
        ctrl1.TabIndex.Returns(0);

        IFocusable ctrl2 = Substitute.For<IFocusable>();
        ctrl2.IsFocusable.Returns(true);
        ctrl2.TabIndex.Returns(1);

        manager.Register(ctrl1);
        manager.Register(ctrl2);
        manager.Focus(ctrl1);

        // Act
        manager.Previous();

        // Assert
        manager.FocusedControl.Should().Be(ctrl2);
    }

    [Fact]
    public void HandleTabInput_WithShiftHeld_InvokesPrevious()
    {
        // Arrange
        FocusManager manager = new();
        IFocusable ctrl1 = Substitute.For<IFocusable>();
        ctrl1.IsFocusable.Returns(true);
        ctrl1.TabIndex.Returns(0);

        IFocusable ctrl2 = Substitute.For<IFocusable>();
        ctrl2.IsFocusable.Returns(true);
        ctrl2.TabIndex.Returns(1);

        manager.Register(ctrl1);
        manager.Register(ctrl2);
        manager.Focus(ctrl1);

        // Act
        manager.HandleTabInput(tabPressed: true, shiftHeld: true);

        // Assert
        manager.FocusedControl.Should().Be(ctrl2);
    }
}
