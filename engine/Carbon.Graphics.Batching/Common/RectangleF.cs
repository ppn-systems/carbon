// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;

namespace Carbon.Graphics.Batching.Common;

/// <summary>
/// Represents a 2D floating-point rectangle in screen or world space.
/// </summary>
/// <param name="X">The X coordinate of the top-left corner.</param>
/// <param name="Y">The Y coordinate of the top-left corner.</param>
/// <param name="Width">The horizontal size of the rectangle.</param>
/// <param name="Height">The vertical size of the rectangle.</param>
public readonly record struct RectangleF(float X, float Y, float Width, float Height)
{
    /// <summary>
    /// Gets the X coordinate of the left edge.
    /// </summary>
    public float Left => this.X;

    /// <summary>
    /// Gets the Y coordinate of the top edge.
    /// </summary>
    public float Top => this.Y;

    /// <summary>
    /// Gets the X coordinate of the right edge.
    /// </summary>
    public float Right => this.X + this.Width;

    /// <summary>
    /// Gets the Y coordinate of the bottom edge.
    /// </summary>
    public float Bottom => this.Y + this.Height;

    /// <summary>
    /// Gets the center point of the rectangle.
    /// </summary>
    public Vector2 Center => new(this.X + (this.Width * 0.5f), this.Y + (this.Height * 0.5f));

    /// <summary>
    /// An empty rectangle with zero position and size.
    /// </summary>
    public static readonly RectangleF Empty = new(0f, 0f, 0f, 0f);

    /// <summary>
    /// Determines whether the rectangle contains the specified point.
    /// </summary>
    /// <param name="point">The 2D point to test.</param>
    /// <returns><see langword="true"/> if the point is inside or on the boundaries; otherwise, <see langword="false"/>.</returns>
    public bool Contains(in Vector2 point) =>
        point.X >= this.Left && point.X <= this.Right && point.Y >= this.Top && point.Y <= this.Bottom;

    /// <summary>
    /// Determines whether this rectangle intersects with another rectangle.
    /// </summary>
    /// <param name="other">The other rectangle to test against.</param>
    /// <returns><see langword="true"/> if the rectangles overlap; otherwise, <see langword="false"/>.</returns>
    public bool Intersects(in RectangleF other) =>
        this.Left < other.Right && this.Right > other.Left && this.Top < other.Bottom && this.Bottom > other.Top;
}
