// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Graphics.Animation.Directions;

/// <summary>
/// Specifies the 8 cardinal and diagonal directions in a 2.5D / 2D top-down coordinate space.
/// </summary>
public enum Direction8
{
    /// <summary>
    /// Facing downwards towards the camera (+Y). Default facing direction.
    /// </summary>
    South = 0,

    /// <summary>
    /// Facing down and right (+X, +Y).
    /// </summary>
    SouthEast = 1,

    /// <summary>
    /// Facing right (+X).
    /// </summary>
    East = 2,

    /// <summary>
    /// Facing up and right (+X, -Y).
    /// </summary>
    NorthEast = 3,

    /// <summary>
    /// Facing upwards away from the camera (-Y).
    /// </summary>
    North = 4,

    /// <summary>
    /// Facing up and left (-X, -Y).
    /// </summary>
    NorthWest = 5,

    /// <summary>
    /// Facing left (-X).
    /// </summary>
    West = 6,

    /// <summary>
    /// Facing down and left (-X, +Y).
    /// </summary>
    SouthWest = 7
}
