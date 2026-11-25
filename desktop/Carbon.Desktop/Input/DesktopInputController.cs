// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Windowing.Abstractions;
using Carbon.Graphics.Windowing.Input;
using Silk.NET.Input;

namespace Carbon.Desktop.Input;

/// <summary>
/// Per-frame snapshot of decoded player movement, zoom, world interaction, and tool inputs.
/// </summary>
/// <param name="MoveDirection">Normalized 2D movement vector.</param>
/// <param name="Sprint">Whether sprint is held.</param>
/// <param name="ZoomMultiplier">Optional continuous zoom step multiplier.</param>
/// <param name="HarvestPressed">Whether harvest interaction was triggered this frame.</param>
/// <param name="ToggleDoorPressed">Whether door toggle was triggered this frame.</param>
/// <param name="PlaceHousePressed">Whether starter house placement was triggered this frame.</param>
/// <param name="ToggleDebugPressed">Whether solid debug overlay toggle was triggered this frame.</param>
/// <param name="ReseedPressed">Whether offline map reseed was triggered this frame.</param>
/// <param name="ScreenshotPressed">Whether manual screenshot capture was triggered this frame.</param>
/// <param name="ExitPressed">Whether exit key was pressed.</param>
internal readonly record struct DesktopInputFrame(
    Vector2 MoveDirection,
    bool Sprint,
    float? ZoomMultiplier,
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

        return new DesktopInputFrame(
            MoveDirection: moveDir,
            Sprint: keyboard.IsKeyDown(Key.ShiftLeft) || keyboard.IsKeyDown(Key.ShiftRight),
            ZoomMultiplier: zoomMultiplier,
            HarvestPressed: keyboard.IsKeyPressed(Key.F),
            ToggleDoorPressed: keyboard.IsKeyPressed(Key.G),
            PlaceHousePressed: keyboard.IsKeyPressed(Key.H),
            ToggleDebugPressed: keyboard.IsKeyPressed(Key.B),
            ReseedPressed: keyboard.IsKeyPressed(Key.R),
            ScreenshotPressed: keyboard.IsKeyPressed(Key.F12),
            ExitPressed: keyboard.IsKeyDown(Key.Escape));
    }
}
