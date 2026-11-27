// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Maps.Landmarks;

/// <summary>
/// A straight stretch of dirt path inside a point of interest (a village street, a side lane, or the short spur from a
/// street to a front door). Visual only: it neither blocks nor slows anything. World tile units.
/// </summary>
/// <param name="X0">Start X.</param>
/// <param name="Y0">Start Y.</param>
/// <param name="X1">End X.</param>
/// <param name="Y1">End Y.</param>
/// <param name="Width">Path width in tiles.</param>
public readonly record struct LandmarkPath(float X0, float Y0, float X1, float Y1, float Width)
{
    /// <summary>Gets the squared distance from a point to the centre line of the path.</summary>
    /// <param name="x">Point X.</param>
    /// <param name="y">Point Y.</param>
    /// <returns>The squared distance in tiles².</returns>
    public float DistanceSquaredTo(float x, float y)
    {
        float dx = this.X1 - this.X0;
        float dy = this.Y1 - this.Y0;
        float len2 = (dx * dx) + (dy * dy);
        float t = len2 <= 1e-6f ? 0f : System.Math.Clamp((((x - this.X0) * dx) + ((y - this.Y0) * dy)) / len2, 0f, 1f);
        float ex = this.X0 + (dx * t) - x;
        float ey = this.Y0 + (dy * t) - y;
        return (ex * ex) + (ey * ey);
    }
}
