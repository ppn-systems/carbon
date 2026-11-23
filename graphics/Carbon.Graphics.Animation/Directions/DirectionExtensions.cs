// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;

namespace Carbon.Graphics.Animation.Directions;

/// <summary>
/// Provides extension and utility methods for directional calculations and conversions.
/// </summary>
public static class DirectionExtensions
{
    private const float InvSqrt2 = 0.70710678f;

    /// <summary>
    /// Converts a 2D velocity or heading vector into the closest <see cref="Direction8"/>.
    /// </summary>
    /// <param name="vector">The movement or facing vector.</param>
    /// <param name="fallback">The fallback direction returned if the vector magnitude is near zero.</param>
    /// <returns>The closest 8-way directional heading.</returns>
    public static Direction8 FromVector(Vector2 vector, Direction8 fallback = Direction8.South)
    {
        if (vector.LengthSquared() < 0.0001f)
        {
            return fallback;
        }

        float deg = MathF.Atan2(vector.Y, vector.X) * (180f / MathF.PI);
        if (deg < 0f)
        {
            deg += 360f;
        }

        int octant = (int)MathF.Floor((deg + 22.5f) / 45f) % 8;
        return octant switch
        {
            0 => Direction8.East,
            1 => Direction8.SouthEast,
            2 => Direction8.South,
            3 => Direction8.SouthWest,
            4 => Direction8.West,
            5 => Direction8.NorthWest,
            6 => Direction8.North,
            7 => Direction8.NorthEast,
            _ => fallback
        };
    }

    /// <summary>
    /// Converts a 2D vector into the closest cardinal <see cref="Direction4"/>.
    /// </summary>
    /// <param name="vector">The movement or facing vector.</param>
    /// <param name="fallback">The fallback direction returned if the vector magnitude is near zero.</param>
    /// <returns>The closest 4-way directional heading.</returns>
    public static Direction4 FromVector4(Vector2 vector, Direction4 fallback = Direction4.South) =>
        ToDirection4(FromVector(vector, (Direction8)fallback));

    /// <summary>
    /// Returns a normalized 2D unit vector representing the specified <see cref="Direction8"/>.
    /// </summary>
    /// <param name="direction">The direction enum value.</param>
    /// <returns>A normalized unit vector.</returns>
    public static Vector2 ToVector(this Direction8 direction) =>
        direction switch
        {
            Direction8.South => new Vector2(0f, 1f),
            Direction8.SouthEast => new Vector2(InvSqrt2, InvSqrt2),
            Direction8.East => new Vector2(1f, 0f),
            Direction8.NorthEast => new Vector2(InvSqrt2, -InvSqrt2),
            Direction8.North => new Vector2(0f, -1f),
            Direction8.NorthWest => new Vector2(-InvSqrt2, -InvSqrt2),
            Direction8.West => new Vector2(-1f, 0f),
            Direction8.SouthWest => new Vector2(-InvSqrt2, InvSqrt2),
            _ => new Vector2(0f, 1f)
        };

    /// <summary>
    /// Returns a normalized 2D unit vector representing the specified <see cref="Direction4"/>.
    /// </summary>
    /// <param name="direction">The direction enum value.</param>
    /// <returns>A normalized unit vector.</returns>
    public static Vector2 ToVector(this Direction4 direction) =>
        direction switch
        {
            Direction4.South => new Vector2(0f, 1f),
            Direction4.West => new Vector2(-1f, 0f),
            Direction4.East => new Vector2(1f, 0f),
            Direction4.North => new Vector2(0f, -1f),
            _ => new Vector2(0f, 1f)
        };

    /// <summary>
    /// Maps an 8-way direction to the closest 4-way cardinal direction.
    /// </summary>
    /// <param name="direction">The 8-way direction.</param>
    /// <returns>The nearest cardinal <see cref="Direction4"/>.</returns>
    public static Direction4 ToDirection4(this Direction8 direction) =>
        direction switch
        {
            Direction8.South => Direction4.South,
            Direction8.SouthEast => Direction4.East,
            Direction8.East => Direction4.East,
            Direction8.NorthEast => Direction4.North,
            Direction8.North => Direction4.North,
            Direction8.NorthWest => Direction4.North,
            Direction8.West => Direction4.West,
            Direction8.SouthWest => Direction4.South,
            _ => Direction4.South
        };

    /// <summary>
    /// Mirrors an 8-way direction horizontally across the vertical axis (e.g. East becomes West).
    /// </summary>
    /// <param name="direction">The direction to mirror.</param>
    /// <returns>The horizontally mirrored direction.</returns>
    public static Direction8 MirrorHorizontally(this Direction8 direction) =>
        direction switch
        {
            Direction8.South => Direction8.South,
            Direction8.SouthEast => Direction8.SouthWest,
            Direction8.East => Direction8.West,
            Direction8.NorthEast => Direction8.NorthWest,
            Direction8.North => Direction8.North,
            Direction8.NorthWest => Direction8.NorthEast,
            Direction8.West => Direction8.East,
            Direction8.SouthWest => Direction8.SouthEast,
            _ => direction
        };

    /// <summary>
    /// Mirrors an 8-way direction vertically across the horizontal axis (e.g. North becomes South).
    /// </summary>
    /// <param name="direction">The direction to mirror.</param>
    /// <returns>The vertically mirrored direction.</returns>
    public static Direction8 MirrorVertically(this Direction8 direction) =>
        direction switch
        {
            Direction8.South => Direction8.North,
            Direction8.SouthEast => Direction8.NorthEast,
            Direction8.East => Direction8.East,
            Direction8.NorthEast => Direction8.SouthEast,
            Direction8.North => Direction8.South,
            Direction8.NorthWest => Direction8.SouthWest,
            Direction8.West => Direction8.West,
            Direction8.SouthWest => Direction8.NorthWest,
            _ => direction
        };
}
