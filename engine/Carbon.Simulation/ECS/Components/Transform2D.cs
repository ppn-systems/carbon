// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;

namespace Carbon.Simulation.ECS.Components;

/// <summary>
/// Represents 2D spatial position and orientation of an entity in world space.
/// Unmanaged struct component.
/// </summary>
/// <param name="Position">2D position vector in world units.</param>
/// <param name="Rotation">Rotation angle in radians.</param>
public record struct Transform2D(Vector2 Position, float Rotation = 0f)
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Transform2D"/> struct from X and Y coordinates.
    /// </summary>
    public Transform2D(float x, float y, float rotation = 0f) : this(new Vector2(x, y), rotation) { }
}
