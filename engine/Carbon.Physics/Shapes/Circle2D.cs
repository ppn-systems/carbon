// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;

namespace Carbon.Physics.Shapes;

/// <summary>
/// Represents a 2D bounding circle.
/// Zero allocation unmanaged struct.
/// </summary>
/// <param name="Center">Center point in world space.</param>
/// <param name="Radius">Circle radius.</param>
public readonly record struct Circle2D(Vector2 Center, float Radius)
{
    /// <summary>
    /// Determines whether this circle overlaps another circle.
    /// </summary>
    /// <param name="other">The other circle to test.</param>
    /// <returns><see langword="true"/> if overlapping; otherwise, <see langword="false"/>.</returns>
    public bool Overlaps(in Circle2D other)
    {
        float dx = this.Center.X - other.Center.X;
        float dy = this.Center.Y - other.Center.Y;
        float rSum = this.Radius + other.Radius;
        return ((dx * dx) + (dy * dy)) <= (rSum * rSum);
    }

    /// <summary>
    /// Determines whether a point lies within this circle.
    /// </summary>
    /// <param name="point">The 2D point to test.</param>
    /// <returns><see langword="true"/> if inside; otherwise, <see langword="false"/>.</returns>
    public bool Contains(Vector2 point)
    {
        float dx = this.Center.X - point.X;
        float dy = this.Center.Y - point.Y;
        return ((dx * dx) + (dy * dy)) <= (this.Radius * this.Radius);
    }
}
