// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;

namespace Carbon.Physics.Raycast;

/// <summary>
/// Contains detailed geometric intersection information from a 2D raycast query.
/// </summary>
public readonly record struct RaycastHit2D(
    bool HasHit,
    float Distance,
    Vector2 Point,
    Vector2 Normal)
{
    /// <summary>
    /// Represents an empty raycast result indicating no intersection.
    /// </summary>
    public static readonly RaycastHit2D None = new(false, 0f, Vector2.Zero, Vector2.Zero);
}
