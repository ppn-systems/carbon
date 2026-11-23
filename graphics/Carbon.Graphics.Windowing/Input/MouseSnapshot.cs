// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Silk.NET.Input;

namespace Carbon.Graphics.Windowing.Input;

/// <summary>
/// Immutable snapshot of the mouse state at a specific frame tick.
/// Provides cursor coordinates, motion deltas, scroll wheel ticks, and button states.
/// </summary>
public readonly struct MouseSnapshot : IEquatable<MouseSnapshot>
{
    private readonly bool[] _currentButtons;
    private readonly bool[] _previousButtons;

    /// <summary>
    /// Gets the current cursor position in window screen coordinates.
    /// </summary>
    public Vector2 Position { get; }

    /// <summary>
    /// Gets the cursor position in the previous frame.
    /// </summary>
    public Vector2 PreviousPosition { get; }

    /// <summary>
    /// Gets the relative cursor movement delta vector (Current - Previous).
    /// </summary>
    public Vector2 Delta => this.Position - this.PreviousPosition;

    /// <summary>
    /// Gets the mouse scroll wheel delta tick vector for this frame.
    /// </summary>
    public Vector2 ScrollDelta { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MouseSnapshot"/> struct.
    /// </summary>
    /// <param name="position">Current mouse position.</param>
    /// <param name="previousPosition">Previous frame mouse position.</param>
    /// <param name="scrollDelta">Wheel scroll delta.</param>
    /// <param name="currentButtons">Current button state array.</param>
    /// <param name="previousButtons">Previous button state array.</param>
    public MouseSnapshot(
        Vector2 position,
        Vector2 previousPosition,
        Vector2 scrollDelta,
        bool[] currentButtons,
        bool[] previousButtons)
    {
        this.Position = position;
        this.PreviousPosition = previousPosition;
        this.ScrollDelta = scrollDelta;
        _currentButtons = currentButtons ?? [];
        _previousButtons = previousButtons ?? [];
    }

    /// <summary>
    /// Determines whether the specified mouse button is currently held down.
    /// </summary>
    /// <param name="button">The mouse button to test.</param>
    /// <returns><see langword="true"/> if held down; otherwise, <see langword="false"/>.</returns>
    public bool IsButtonDown(MouseButton button)
    {
        int idx = (int)button;
        return idx >= 0 && idx < _currentButtons.Length && _currentButtons[idx];
    }

    /// <summary>
    /// Determines whether the specified mouse button is currently up.
    /// </summary>
    /// <param name="button">The mouse button to test.</param>
    /// <returns><see langword="true"/> if up; otherwise, <see langword="false"/>.</returns>
    public bool IsButtonUp(MouseButton button) => !this.IsButtonDown(button);

    /// <summary>
    /// Determines whether the specified mouse button was newly clicked down in this frame.
    /// </summary>
    /// <param name="button">The mouse button to test.</param>
    /// <returns><see langword="true"/> if newly clicked down; otherwise, <see langword="false"/>.</returns>
    public bool IsButtonPressed(MouseButton button)
    {
        int idx = (int)button;
        return idx >= 0 && idx < _currentButtons.Length && _currentButtons[idx] && !_previousButtons[idx];
    }

    /// <summary>
    /// Determines whether the specified mouse button was newly released in this frame.
    /// </summary>
    /// <param name="button">The mouse button to test.</param>
    /// <returns><see langword="true"/> if newly released; otherwise, <see langword="false"/>.</returns>
    public bool IsButtonReleased(MouseButton button)
    {
        int idx = (int)button;
        return idx >= 0 && idx < _currentButtons.Length && !_currentButtons[idx] && _previousButtons[idx];
    }

    /// <inheritdoc/>
    public bool Equals(MouseSnapshot other) =>
        this.Position.Equals(other.Position)
        && this.PreviousPosition.Equals(other.PreviousPosition)
        && this.ScrollDelta.Equals(other.ScrollDelta)
        && ReferenceEquals(_currentButtons, other._currentButtons)
        && ReferenceEquals(_previousButtons, other._previousButtons);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is MouseSnapshot other && this.Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(this.Position, this.PreviousPosition, this.ScrollDelta, _currentButtons, _previousButtons);

    /// <summary>
    /// Compares two <see cref="MouseSnapshot"/> instances for equality.
    /// </summary>
    public static bool operator ==(MouseSnapshot left, MouseSnapshot right) => left.Equals(right);

    /// <summary>
    /// Compares two <see cref="MouseSnapshot"/> instances for inequality.
    /// </summary>
    public static bool operator !=(MouseSnapshot left, MouseSnapshot right) => !left.Equals(right);
}
