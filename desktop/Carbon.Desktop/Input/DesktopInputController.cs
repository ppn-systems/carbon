// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Windowing.Abstractions;
using Carbon.Graphics.Windowing.Input;
using Silk.NET.Input;

namespace Carbon.Desktop.Input;

/// <summary>
/// Per-frame snapshot of decoded player movement, combat, hotbar/crafting, zoom, world interaction, and tool inputs.
/// </summary>
internal readonly record struct DesktopInputFrame(
    Vector2 MoveDirection,
    bool Sprint,
    float? ZoomMultiplier,
    bool SpaceActionPressed,
    bool LeftClickPressed,
    int ScrollSlotDelta,
    Vector2 MouseScreenPosition,
    byte? NumberSlotPressed,
    bool ToggleCraftPressed,
    bool ToggleHudPressed,
    bool HarvestPressed,
    bool ToggleDoorPressed,
    bool PlaceHousePressed,
    bool ToggleDebugPressed,
    bool ReseedPressed,
    bool ScreenshotPressed,
    bool ExitPressed);

/// <summary>
/// Decodes raw keyboard and mouse state from <see cref="IInputManager"/> into game actions.
/// </summary>
internal static class DesktopInputController
{
    public static DesktopInputFrame Poll(IInputManager input)
    {
        ArgumentNullException.ThrowIfNull(input);

        KeyboardSnapshot keyboard = input.Keyboard;
        MouseSnapshot mouse = input.Mouse;

        Vector2 moveDir = Vector2.Zero;
        if (keyboard.IsKeyDown(Key.W) || keyboard.IsKeyDown(Key.Up))
        {
            moveDir.Y -= 1f;
        }

        if (keyboard.IsKeyDown(Key.S) || keyboard.IsKeyDown(Key.Down))
        {
            moveDir.Y += 1f;
        }

        if (keyboard.IsKeyDown(Key.A) || keyboard.IsKeyDown(Key.Left))
        {
            moveDir.X -= 1f;
        }

        if (keyboard.IsKeyDown(Key.D) || keyboard.IsKeyDown(Key.Right))
        {
            moveDir.X += 1f;
        }

        float? zoomMultiplier = null;
        if (keyboard.IsKeyDown(Key.Q))
        {
            zoomMultiplier = 0.985f;
        }
        else if (keyboard.IsKeyDown(Key.E))
        {
            zoomMultiplier = 1.015f;
        }

        byte? numberSlot = PollNumberKey(keyboard);
        int scrollDelta = mouse.ScrollDelta.Y > 0.01f
            ? -1
            : (mouse.ScrollDelta.Y < -0.01f ? 1 : 0);

        return new DesktopInputFrame(
            MoveDirection: moveDir,
            Sprint: keyboard.IsKeyDown(Key.ShiftLeft) || keyboard.IsKeyDown(Key.ShiftRight),
            ZoomMultiplier: zoomMultiplier,
            SpaceActionPressed: keyboard.IsKeyPressed(Key.Space),
            LeftClickPressed: mouse.IsButtonPressed(MouseButton.Left),
            ScrollSlotDelta: scrollDelta,
            MouseScreenPosition: mouse.Position,
            NumberSlotPressed: numberSlot,
            ToggleCraftPressed: keyboard.IsKeyPressed(Key.C),
            ToggleHudPressed: keyboard.IsKeyPressed(Key.Tab),
            HarvestPressed: keyboard.IsKeyPressed(Key.F),
            ToggleDoorPressed: keyboard.IsKeyPressed(Key.G),
            PlaceHousePressed: keyboard.IsKeyPressed(Key.H),
            ToggleDebugPressed: keyboard.IsKeyPressed(Key.B),
            ReseedPressed: keyboard.IsKeyPressed(Key.R),
            ScreenshotPressed: keyboard.IsKeyPressed(Key.F12),
            ExitPressed: keyboard.IsKeyDown(Key.Escape));
    }

    private static byte? PollNumberKey(in KeyboardSnapshot keyboard)
    {
        if (keyboard.IsKeyPressed(Key.Number1) || keyboard.IsKeyPressed(Key.Keypad1))
        {
            return 0;
        }

        if (keyboard.IsKeyPressed(Key.Number2) || keyboard.IsKeyPressed(Key.Keypad2))
        {
            return 1;
        }

        if (keyboard.IsKeyPressed(Key.Number3) || keyboard.IsKeyPressed(Key.Keypad3))
        {
            return 2;
        }

        if (keyboard.IsKeyPressed(Key.Number4) || keyboard.IsKeyPressed(Key.Keypad4))
        {
            return 3;
        }

        if (keyboard.IsKeyPressed(Key.Number5) || keyboard.IsKeyPressed(Key.Keypad5))
        {
            return 4;
        }

        if (keyboard.IsKeyPressed(Key.Number6) || keyboard.IsKeyPressed(Key.Keypad6))
        {
            return 5;
        }

        if (keyboard.IsKeyPressed(Key.Number7) || keyboard.IsKeyPressed(Key.Keypad7))
        {
            return 6;
        }

        if (keyboard.IsKeyPressed(Key.Number8) || keyboard.IsKeyPressed(Key.Keypad8))
        {
            return 7;
        }

        if (keyboard.IsKeyPressed(Key.Number9) || keyboard.IsKeyPressed(Key.Keypad9))
        {
            return 8;
        }

        return null;
    }
}
