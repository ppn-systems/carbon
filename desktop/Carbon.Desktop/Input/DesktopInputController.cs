// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Windowing.Abstractions;
using Carbon.Graphics.Windowing.Input;
using Carbon.Items.Catalog;
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
    bool LeftMouseDown,
    bool RightClickPressed,
    bool RightMouseDown,
    bool ShiftHeld,
    int ScrollSlotDelta,
    Vector2 MouseScreenPosition,
    byte? NumberSlotPressed,
    ushort? SkillPressed,
    bool ToggleInventoryPressed,
    bool ToggleCharacterPressed,
    bool CycleTargetPressed,
    bool InteractOrPickupPressed,
    bool DropItemPressed,
    bool ToggleCraftPressed,
    bool ToggleForgePressed,
    bool ToggleSkillsPressed,
    bool ToggleMinimapPressed,
    bool ToggleRestPressed,
    bool ToggleHudPressed,
    bool HarvestPressed,
    bool ToggleDoorPressed,
    bool PlaceStructurePressed,
    bool DemolishStructurePressed,
    bool ToggleDebugPressed,
    bool ScreenshotPressed,
    bool EscapePressed,
    bool CtrlHeld = false,
    bool DeletePressed = false,
    bool ConfirmPressed = false,
    bool ToggleRepairPressed = false);

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
        if (keyboard.IsKeyDown(Key.Minus) || keyboard.IsKeyDown(Key.KeypadSubtract) || keyboard.IsKeyDown(Key.PageDown))
        {
            zoomMultiplier = 0.985f;
        }
        else if (keyboard.IsKeyDown(Key.Equal) || keyboard.IsKeyDown(Key.KeypadAdd) || keyboard.IsKeyDown(Key.PageUp))
        {
            zoomMultiplier = 1.015f;
        }

        byte? numberSlot = PollNumberKey(keyboard);
        ushort? skillPressed = keyboard.IsKeyPressed(Key.Q)
            ? SkillCatalog.WhirlwindId
            : (keyboard.IsKeyPressed(Key.R) ? SkillCatalog.ArcaneNovaId : (keyboard.IsKeyPressed(Key.V) ? SkillCatalog.VitalSurgeId : null));
        int scrollDelta = mouse.ScrollDelta.Y > 0.01f
            ? -1
            : (mouse.ScrollDelta.Y < -0.01f ? 1 : 0);

        bool shiftHeld = keyboard.IsKeyDown(Key.ShiftLeft) || keyboard.IsKeyDown(Key.ShiftRight);
        return new DesktopInputFrame(
            MoveDirection: moveDir,
            Sprint: shiftHeld,
            ZoomMultiplier: zoomMultiplier,
            SpaceActionPressed: keyboard.IsKeyPressed(Key.Space),
            LeftClickPressed: mouse.IsButtonPressed(MouseButton.Left),
            LeftMouseDown: mouse.IsButtonDown(MouseButton.Left),
            RightClickPressed: mouse.IsButtonPressed(MouseButton.Right),
            RightMouseDown: mouse.IsButtonDown(MouseButton.Right),
            ShiftHeld: shiftHeld,
            ScrollSlotDelta: scrollDelta,
            MouseScreenPosition: mouse.Position,
            NumberSlotPressed: numberSlot,
            SkillPressed: skillPressed,
            ToggleInventoryPressed: keyboard.IsKeyPressed(Key.I) || keyboard.IsKeyPressed(Key.B),
            ToggleCharacterPressed: keyboard.IsKeyPressed(Key.Y),
            CycleTargetPressed: keyboard.IsKeyPressed(Key.Tab),
            InteractOrPickupPressed: keyboard.IsKeyPressed(Key.E),
            DropItemPressed: keyboard.IsKeyPressed(Key.X),
            ToggleCraftPressed: keyboard.IsKeyPressed(Key.C),
            ToggleForgePressed: keyboard.IsKeyPressed(Key.U),
            ToggleSkillsPressed: keyboard.IsKeyPressed(Key.K),
            ToggleMinimapPressed: keyboard.IsKeyPressed(Key.M),
            ToggleRestPressed: keyboard.IsKeyPressed(Key.T),
            ToggleHudPressed: keyboard.IsKeyPressed(Key.F1),
            HarvestPressed: keyboard.IsKeyPressed(Key.F),
            ToggleDoorPressed: keyboard.IsKeyPressed(Key.G),
            PlaceStructurePressed: keyboard.IsKeyPressed(Key.H),
            DemolishStructurePressed: keyboard.IsKeyPressed(Key.Z),
            ToggleDebugPressed: keyboard.IsKeyPressed(Key.F3),
            ScreenshotPressed: keyboard.IsKeyPressed(Key.F12),
            EscapePressed: keyboard.IsKeyPressed(Key.Escape),
            CtrlHeld: keyboard.IsKeyDown(Key.ControlLeft) || keyboard.IsKeyDown(Key.ControlRight),
            DeletePressed: keyboard.IsKeyPressed(Key.Delete),
            ConfirmPressed: keyboard.IsKeyPressed(Key.Enter) || keyboard.IsKeyPressed(Key.KeypadEnter),
            ToggleRepairPressed: keyboard.IsKeyPressed(Key.N));
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
