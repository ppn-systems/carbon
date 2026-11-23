// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Silk.NET.Input;

namespace Carbon.Graphics.Windowing.Input;

/// <summary>
/// Defines the contract for polling and querying keyboard, mouse, and text character input states.
/// </summary>
public interface IInputManager
{
    /// <summary>
    /// Gets the current frame keyboard snapshot.
    /// </summary>
    KeyboardSnapshot Keyboard { get; }

    /// <summary>
    /// Gets the current frame mouse snapshot.
    /// </summary>
    MouseSnapshot Mouse { get; }

    /// <summary>
    /// Gets the current mouse cursor screen coordinates.
    /// </summary>
    Vector2 MousePosition => this.Mouse.Position;

    /// <summary>
    /// Gets the mouse movement delta vector for the current frame.
    /// </summary>
    Vector2 MouseDelta => this.Mouse.Delta;

    /// <summary>
    /// Gets the mouse wheel scroll delta vector for the current frame.
    /// </summary>
    Vector2 ScrollDelta => this.Mouse.ScrollDelta;

    /// <summary>
    /// Occurs when a printable text character is received from the operating system (for text box input).
    /// </summary>
    event Action<char>? CharacterReceived;

    /// <summary>
    /// Checks whether the specified keyboard key is held down.
    /// </summary>
    /// <param name="key">The key to query.</param>
    /// <returns><see langword="true"/> if held down; otherwise, <see langword="false"/>.</returns>
    bool IsKeyDown(Key key);

    /// <summary>
    /// Checks whether the specified keyboard key was pressed down during this frame.
    /// </summary>
    /// <param name="key">The key to query.</param>
    /// <returns><see langword="true"/> if pressed down; otherwise, <see langword="false"/>.</returns>
    bool IsKeyPressed(Key key);

    /// <summary>
    /// Checks whether the specified keyboard key was released during this frame.
    /// </summary>
    /// <param name="key">The key to query.</param>
    /// <returns><see langword="true"/> if released; otherwise, <see langword="false"/>.</returns>
    bool IsKeyReleased(Key key);

    /// <summary>
    /// Checks whether the specified mouse button is held down.
    /// </summary>
    /// <param name="button">The mouse button to query.</param>
    /// <returns><see langword="true"/> if held down; otherwise, <see langword="false"/>.</returns>
    bool IsMouseButtonDown(MouseButton button);

    /// <summary>
    /// Checks whether the specified mouse button was clicked down during this frame.
    /// </summary>
    /// <param name="button">The mouse button to query.</param>
    /// <returns><see langword="true"/> if clicked down; otherwise, <see langword="false"/>.</returns>
    bool IsMouseButtonPressed(MouseButton button);

    /// <summary>
    /// Checks whether the specified mouse button was released during this frame.
    /// </summary>
    /// <param name="button">The mouse button to query.</param>
    /// <returns><see langword="true"/> if released; otherwise, <see langword="false"/>.</returns>
    bool IsMouseButtonReleased(MouseButton button);
}
