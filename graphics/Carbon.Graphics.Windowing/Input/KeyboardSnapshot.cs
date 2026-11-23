// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Silk.NET.Input;

namespace Carbon.Graphics.Windowing.Input;

/// <summary>
/// Immutable snapshot of the keyboard state at a specific frame tick.
/// Provides zero-allocation querying for key press, down, and release states.
/// </summary>
public readonly struct KeyboardSnapshot : IEquatable<KeyboardSnapshot>
{
    private readonly bool[] _current;
    private readonly bool[] _previous;

    /// <summary>
    /// Initializes a new instance of the <see cref="KeyboardSnapshot"/> struct.
    /// </summary>
    /// <param name="current">The current key state array.</param>
    /// <param name="previous">The previous frame key state array.</param>
    public KeyboardSnapshot(bool[] current, bool[] previous)
    {
        _current = current ?? [];
        _previous = previous ?? [];
    }

    /// <summary>
    /// Determines whether the specified key is currently held down.
    /// </summary>
    /// <param name="key">The keyboard key to test.</param>
    /// <returns><see langword="true"/> if held down; otherwise, <see langword="false"/>.</returns>
    public bool IsKeyDown(Key key)
    {
        int idx = (int)key;
        return idx >= 0 && idx < _current.Length && _current[idx];
    }

    /// <summary>
    /// Determines whether the specified key is currently up (released).
    /// </summary>
    /// <param name="key">The keyboard key to test.</param>
    /// <returns><see langword="true"/> if up; otherwise, <see langword="false"/>.</returns>
    public bool IsKeyUp(Key key) => !this.IsKeyDown(key);

    /// <summary>
    /// Determines whether the specified key was pressed down in the current frame (transitioned from up to down).
    /// </summary>
    /// <param name="key">The keyboard key to test.</param>
    /// <returns><see langword="true"/> if newly pressed; otherwise, <see langword="false"/>.</returns>
    public bool IsKeyPressed(Key key)
    {
        int idx = (int)key;
        return idx >= 0 && idx < _current.Length && _current[idx] && !_previous[idx];
    }

    /// <summary>
    /// Determines whether the specified key was released in the current frame (transitioned from down to up).
    /// </summary>
    /// <param name="key">The keyboard key to test.</param>
    /// <returns><see langword="true"/> if newly released; otherwise, <see langword="false"/>.</returns>
    public bool IsKeyReleased(Key key)
    {
        int idx = (int)key;
        return idx >= 0 && idx < _current.Length && !_current[idx] && _previous[idx];
    }

    /// <inheritdoc/>
    public bool Equals(KeyboardSnapshot other) =>
        ReferenceEquals(_current, other._current) && ReferenceEquals(_previous, other._previous);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is KeyboardSnapshot other && this.Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(_current, _previous);

    /// <summary>
    /// Compares two <see cref="KeyboardSnapshot"/> instances for equality.
    /// </summary>
    public static bool operator ==(KeyboardSnapshot left, KeyboardSnapshot right) => left.Equals(right);

    /// <summary>
    /// Compares two <see cref="KeyboardSnapshot"/> instances for inequality.
    /// </summary>
    public static bool operator !=(KeyboardSnapshot left, KeyboardSnapshot right) => !left.Equals(right);
}
