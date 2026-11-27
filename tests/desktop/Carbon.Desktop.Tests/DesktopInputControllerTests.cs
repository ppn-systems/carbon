// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Desktop.Input;
using Carbon.Graphics.Windowing.Abstractions;
using Carbon.Graphics.Windowing.Input;
using FluentAssertions;
using Silk.NET.Input;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>
/// Keyboard and mouse to <see cref="DesktopInputFrame"/> mapping. Every gameplay and inventory action reads this
/// frame, so the important property is edge-triggering: a held key fires its action once, not every frame.
/// </summary>
public sealed class DesktopInputControllerTests
{
    [Fact]
    public void Poll_OpposingMovementKeys_CancelOut()
    {
        DesktopInputFrame frame = DesktopInputController.Poll(Input.Held(Key.W, Key.S, Key.D));

        frame.MoveDirection.Should().Be(new Vector2(1f, 0f), "up and down cancel, right remains");
    }

    [Fact]
    public void Poll_ArrowKeysAndWasd_MoveTheSameWay()
    {
        DesktopInputController.Poll(Input.Held(Key.Up, Key.Left)).MoveDirection
            .Should().Be(DesktopInputController.Poll(Input.Held(Key.W, Key.A)).MoveDirection);
    }

    [Fact]
    public void Poll_DeleteHeldAcrossFrames_FiresOnlyOnTheFirstFrame()
    {
        DesktopInputController.Poll(Input.Pressed(Key.Delete)).DeletePressed.Should().BeTrue();

        DesktopInputController.Poll(Input.Held(Key.Delete)).DeletePressed
            .Should().BeFalse("holding Delete must not reopen the destroy confirmation every frame");
    }

    [Theory]
    [InlineData(Key.Enter)]
    [InlineData(Key.KeypadEnter)]
    public void Poll_EitherEnterKey_Confirms(Key key)
    {
        DesktopInputController.Poll(Input.Pressed(key)).ConfirmPressed.Should().BeTrue();
        DesktopInputController.Poll(Input.Held(key)).ConfirmPressed.Should().BeFalse("a held Enter confirms one dialog, not the next one too");
    }

    [Theory]
    [InlineData(Key.Number1, 0)]
    [InlineData(Key.Number5, 4)]
    [InlineData(Key.Number9, 8)]
    [InlineData(Key.Keypad1, 0)]
    [InlineData(Key.Keypad9, 8)]
    public void Poll_NumberKey_SelectsZeroBasedHotbarSlot(Key key, int slot)
    {
        DesktopInputController.Poll(Input.Pressed(key)).NumberSlotPressed.Should().Be((byte)slot);
        DesktopInputController.Poll(Input.Held(key)).NumberSlotPressed.Should().BeNull("a held number key binds or selects once");
    }

    [Fact]
    public void Poll_NoNumberKey_SelectsNoSlot() =>
        DesktopInputController.Poll(Input.Pressed(Key.Number0)).NumberSlotPressed.Should().BeNull("0 is not a hotbar key");

    [Theory]
    [InlineData(Key.ShiftLeft)]
    [InlineData(Key.ShiftRight)]
    public void Poll_EitherShift_HoldsShiftAndSprints(Key key)
    {
        DesktopInputFrame frame = DesktopInputController.Poll(Input.Held(key));

        frame.ShiftHeld.Should().BeTrue();
        frame.Sprint.Should().BeTrue();
    }

    [Theory]
    [InlineData(Key.ControlLeft)]
    [InlineData(Key.ControlRight)]
    public void Poll_EitherControl_HoldsCtrl(Key key) =>
        DesktopInputController.Poll(Input.Held(key)).CtrlHeld.Should().BeTrue("ctrl-click opens the split dialog");

    [Theory]
    [InlineData(1f, -1)]
    [InlineData(-1f, 1)]
    [InlineData(0.005f, 0)]
    public void Poll_ScrollWheel_StepsHotbarSelection(float scrollY, int expectedDelta) =>
        DesktopInputController.Poll(Input.Scrolled(scrollY)).ScrollSlotDelta.Should().Be(expectedDelta);

    [Fact]
    public void Poll_LeftButtonHeld_IsDownButNotPressedAgain()
    {
        DesktopInputFrame first = DesktopInputController.Poll(Input.Clicked(MouseButton.Left, held: false));
        DesktopInputFrame second = DesktopInputController.Poll(Input.Clicked(MouseButton.Left, held: true));

        first.LeftClickPressed.Should().BeTrue();
        first.LeftMouseDown.Should().BeTrue();
        second.LeftClickPressed.Should().BeFalse("a drag must not pick the item up again every frame");
        second.LeftMouseDown.Should().BeTrue("the inventory drag reads the held state");
    }

    [Fact]
    public void Poll_NothingPressed_ProducesAnIdleFrame()
    {
        DesktopInputFrame frame = DesktopInputController.Poll(Input.Held());

        frame.MoveDirection.Should().Be(Vector2.Zero);
        frame.NumberSlotPressed.Should().BeNull();
        frame.SkillPressed.Should().BeNull();
        frame.ZoomMultiplier.Should().BeNull();
        frame.LeftClickPressed.Should().BeFalse();
        frame.DeletePressed.Should().BeFalse();
    }

    private sealed class Input(KeyboardSnapshot keyboard, MouseSnapshot mouse) : IInputManager
    {
        private const int KeySlots = 512;
        private const int ButtonSlots = 16;

        public event Action<char>? CharacterReceived
        {
            add { }
            remove { }
        }

        public KeyboardSnapshot Keyboard { get; } = keyboard;

        public MouseSnapshot Mouse { get; } = mouse;

        /// <summary>Keys down this frame and the previous one (held).</summary>
        public static Input Held(params Key[] keys) => new(new KeyboardSnapshot(Keys(keys), Keys(keys)), IdleMouse());

        /// <summary>Keys down this frame only (just pressed).</summary>
        public static Input Pressed(params Key[] keys) => new(new KeyboardSnapshot(Keys(keys), Keys()), IdleMouse());

        public static Input Scrolled(float scrollY) =>
            new(new KeyboardSnapshot(Keys(), Keys()), new MouseSnapshot(Vector2.Zero, Vector2.Zero, new Vector2(0f, scrollY), Buttons(), Buttons()));

        public static Input Clicked(MouseButton button, bool held) =>
            new(new KeyboardSnapshot(Keys(), Keys()), new MouseSnapshot(Vector2.Zero, Vector2.Zero, Vector2.Zero, Buttons(button), held ? Buttons(button) : Buttons()));

        public bool IsKeyDown(Key key) => this.Keyboard.IsKeyDown(key);

        public bool IsKeyPressed(Key key) => this.Keyboard.IsKeyPressed(key);

        public bool IsKeyReleased(Key key) => this.Keyboard.IsKeyReleased(key);

        public bool IsMouseButtonDown(MouseButton button) => this.Mouse.IsButtonDown(button);

        public bool IsMouseButtonPressed(MouseButton button) => this.Mouse.IsButtonPressed(button);

        public bool IsMouseButtonReleased(MouseButton button) => this.Mouse.IsButtonReleased(button);

        private static MouseSnapshot IdleMouse() => new(Vector2.Zero, Vector2.Zero, Vector2.Zero, Buttons(), Buttons());

        private static bool[] Keys(params Key[] keys)
        {
            bool[] state = new bool[KeySlots];
            foreach (Key key in keys)
            {
                state[(int)key] = true;
            }

            return state;
        }

        private static bool[] Buttons(params MouseButton[] buttons)
        {
            bool[] state = new bool[ButtonSlots];
            foreach (MouseButton button in buttons)
            {
                state[(int)button] = true;
            }

            return state;
        }
    }
}
