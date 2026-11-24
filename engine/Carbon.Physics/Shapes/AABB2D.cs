// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;

namespace Carbon.Physics.Shapes;

/// <summary>
/// Represents a 2D Axis-Aligned Bounding Box (AABB).
/// Zero allocation unmanaged struct.
/// </summary>
/// <param name="Min">The minimum point boundary.</param>
/// <param name="Max">The maximum point boundary.</param>
public readonly record struct AABB2D(Vector2 Min, Vector2 Max)
{
    /// <summary>
    /// Gets the center point of the bounding box.
    /// </summary>
    public Vector2 Center => (this.Min + this.Max) * 0.5f;

    /// <summary>
    /// Gets the half-dimensions (extents) of the bounding box.
    /// </summary>
    public Vector2 Extents => (this.Max - this.Min) * 0.5f;

    /// <summary>
    /// Gets the width of the bounding box.
    /// </summary>
    public float Width => this.Max.X - this.Min.X;

    /// <summary>
    /// Gets the height of the bounding box.
    /// </summary>
    public float Height => this.Max.Y - this.Min.Y;

    /// <summary>
    /// Determines whether this bounding box overlaps with another AABB.
    /// </summary>
    /// <param name="other">The other AABB to test.</param>
    /// <returns><see langword="true"/> if overlapping; otherwise, <see langword="false"/>.</returns>
    public bool Overlaps(in AABB2D other) =>
        this.Min.X <= other.Max.X && this.Max.X >= other.Min.X &&
        this.Min.Y <= other.Max.Y && this.Max.Y >= other.Min.Y;

    /// <summary>
    /// Determines whether a point lies within this bounding box.
    /// </summary>
    /// <param name="point">The 2D point to test.</param>
    /// <returns><see langword="true"/> if inside; otherwise, <see langword="false"/>.</returns>
    public bool Contains(Vector2 point) =>
        point.X >= this.Min.X && point.X <= this.Max.X &&
        point.Y >= this.Min.Y && point.Y <= this.Max.Y;

    /// <summary>
    /// Offsets this bounding box by a displacement vector.
    /// </summary>
    /// <param name="offset">Displacement translation.</param>
    /// <returns>A new translated AABB.</returns>
    public AABB2D Offset(Vector2 offset) =>
        new(this.Min + offset, this.Max + offset);

    /// <summary>
    /// Creates an AABB from a center point and full width/height size.
    /// </summary>
    /// <param name="center">World center position.</param>
    /// <param name="size">Width and height.</param>
    /// <returns>A new <see cref="AABB2D"/> instance.</returns>
    public static AABB2D FromCenterSize(Vector2 center, Vector2 size)
    {
        Vector2 half = size * 0.5f;
        return new AABB2D(center - half, center + half);
    }
}
