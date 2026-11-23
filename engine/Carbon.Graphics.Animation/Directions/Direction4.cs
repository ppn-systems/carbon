// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Graphics.Animation.Directions;

/// <summary>
/// Specifies the 4 cardinal directions in a 2D top-down coordinate space.
/// </summary>
public enum Direction4
{
    /// <summary>
    /// Facing downwards towards the screen (+Y).
    /// </summary>
    South = 0,

    /// <summary>
    /// Facing left (-X).
    /// </summary>
    West = 1,

    /// <summary>
    /// Facing right (+X).
    /// </summary>
    East = 2,

    /// <summary>
    /// Facing upwards away from the screen (-Y).
    /// </summary>
    North = 3
}
