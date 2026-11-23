// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;

namespace Carbon.Physics.Shapes;

/// <summary>
/// Represents a 2D semi-infinite or bounded ray defined by an origin, normalized direction unit vector, and maximum distance.
/// </summary>
public readonly record struct Ray2D
{
    /// <summary>
    /// Gets the starting point of the ray in world coordinates.
    /// </summary>
    public Vector2 Origin { get; }

    /// <summary>
    /// Gets the normalized direction unit vector of the ray.
    /// </summary>
    public Vector2 Direction { get; }

    /// <summary>
    /// Gets the maximum distance along the ray to test for collisions.
    /// </summary>
    public float MaxDistance { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Ray2D"/> struct.
    /// </summary>
    /// <param name="origin">Starting point in world coordinates.</param>
    /// <param name="direction">Direction vector (will be normalized automatically).</param>
    /// <param name="maxDistance">Maximum test distance.</param>
    public Ray2D(Vector2 origin, Vector2 direction, float maxDistance = float.PositiveInfinity)
    {
        this.Origin = origin;
        float lengthSq = direction.LengthSquared();
        this.Direction = lengthSq > 1e-6f ? Vector2.Normalize(direction) : Vector2.UnitX;
        this.MaxDistance = maxDistance;
    }

    /// <summary>
    /// Evaluates the point along the ray at the specified distance.
    /// </summary>
    /// <param name="distance">Distance from origin.</param>
    /// <returns>World coordinate point.</returns>
    public Vector2 GetPoint(float distance) => this.Origin + (this.Direction * distance);
}
