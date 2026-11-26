// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Windowing.Input;
using FluentAssertions;
using NSubstitute;
using Silk.NET.Input;
using Xunit;

namespace Carbon.Graphics.Windowing.Tests;

public sealed class InputManagerTests
{
    private static (IInputContext Context, IKeyboard Keyboard, IMouse Mouse) CreateFakeInputContext()
    {
        IInputContext context = Substitute.For<IInputContext>();
        IKeyboard keyboard = Substitute.For<IKeyboard>();
        IMouse mouse = Substitute.For<IMouse>();
        mouse.Position.Returns(Vector2.Zero);

        context.Keyboards.Returns([keyboard]);
        context.Mice.Returns([mouse]);

        return (context, keyboard, mouse);
    }

    [Fact]
    public void Update_WhenKeyDownArrivesBeforeTick_ExposesPressedOnFirstTickAndHeldOnSecond()
    {
        // Arrange
        (IInputContext context, IKeyboard keyboard, _) = CreateFakeInputContext();
        using InputManager input = new(context);

        // Act - simulate Silk.NET delivering KeyDown before GameWindow.OnUpdate calls InputManager.Update()
        keyboard.KeyDown += Raise.Event<Action<IKeyboard, Key, int>>(keyboard, Key.Number1, 0);
        input.Update();

        // Assert Frame 1: edge press detected
        input.Keyboard.IsKeyPressed(Key.Number1).Should().BeTrue();
        input.Keyboard.IsKeyDown(Key.Number1).Should().BeTrue();
        input.Keyboard.IsKeyReleased(Key.Number1).Should().BeFalse();

        // Act Frame 2: held without new events
        input.Update();

        // Assert Frame 2: no longer pressed, still down
        input.Keyboard.IsKeyPressed(Key.Number1).Should().BeFalse();
        input.Keyboard.IsKeyDown(Key.Number1).Should().BeTrue();

        // Act Frame 3: KeyUp before Update()
        keyboard.KeyUp += Raise.Event<Action<IKeyboard, Key, int>>(keyboard, Key.Number1, 0);
        input.Update();

        // Assert Frame 3: edge release detected
        input.Keyboard.IsKeyPressed(Key.Number1).Should().BeFalse();
        input.Keyboard.IsKeyDown(Key.Number1).Should().BeFalse();
        input.Keyboard.IsKeyReleased(Key.Number1).Should().BeTrue();
    }

    [Fact]
    public void Update_WhenSubFrameTapOccursBeforeTick_StillExposesPressedOnTick()
    {
        // Arrange
        (IInputContext context, IKeyboard keyboard, _) = CreateFakeInputContext();
        using InputManager input = new(context);

        // Act - both press and release arrive before a single Update()
        keyboard.KeyDown += Raise.Event<Action<IKeyboard, Key, int>>(keyboard, Key.Space, 0);
        keyboard.KeyUp += Raise.Event<Action<IKeyboard, Key, int>>(keyboard, Key.Space, 0);
        input.Update();

        // Assert Frame 1
        input.Keyboard.IsKeyPressed(Key.Space).Should().BeTrue();
        input.Keyboard.IsKeyDown(Key.Space).Should().BeTrue();

        // Act Frame 2
        input.Update();

        // Assert Frame 2
        input.Keyboard.IsKeyPressed(Key.Space).Should().BeFalse();
        input.Keyboard.IsKeyDown(Key.Space).Should().BeFalse();
        input.Keyboard.IsKeyReleased(Key.Space).Should().BeTrue();
    }

    [Fact]
    public void Update_WhenMouseDownArrivesBeforeTick_ExposesMouseButtonPressedAndDelta()
    {
        // Arrange
        (IInputContext context, _, IMouse mouse) = CreateFakeInputContext();
        using InputManager input = new(context);

        // Act
        mouse.MouseMove += Raise.Event<Action<IMouse, Vector2>>(mouse, new Vector2(120f, 80f));
        mouse.MouseDown += Raise.Event<Action<IMouse, MouseButton>>(mouse, MouseButton.Left);
        input.Update();

        // Assert Frame 1
        input.Mouse.IsButtonPressed(MouseButton.Left).Should().BeTrue();
        input.Mouse.IsButtonDown(MouseButton.Left).Should().BeTrue();
        input.Mouse.Position.Should().Be(new Vector2(120f, 80f));
        input.Mouse.Delta.Should().Be(new Vector2(120f, 80f));

        // Act Frame 2
        mouse.MouseUp += Raise.Event<Action<IMouse, MouseButton>>(mouse, MouseButton.Left);
        input.Update();

        // Assert Frame 2
        input.Mouse.IsButtonPressed(MouseButton.Left).Should().BeFalse();
        input.Mouse.IsButtonReleased(MouseButton.Left).Should().BeTrue();
        input.Mouse.Delta.Should().Be(Vector2.Zero);
    }
}
