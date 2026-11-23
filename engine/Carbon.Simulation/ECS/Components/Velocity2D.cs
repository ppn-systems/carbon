// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;

namespace Carbon.Simulation.ECS.Components;

/// <summary>
/// Represents kinematic velocity and linear drag applied during movement integration.
/// Unmanaged struct component.
/// </summary>
/// <param name="Linear">Linear velocity vector in world units per second.</param>
/// <param name="Drag">Linear drag coefficient per second (0.0 for zero deceleration).</param>
public record struct Velocity2D(Vector2 Linear, float Drag = 0f)
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Velocity2D"/> struct from linear components.
    /// </summary>
    public Velocity2D(float vx, float vy, float drag = 0f) : this(new Vector2(vx, vy), drag) { }
}
