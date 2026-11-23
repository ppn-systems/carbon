// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Physics.Shapes;

namespace Carbon.Simulation.ECS.Components;

/// <summary>
/// Specifies the geometric primitive shape used for entity collision.
/// </summary>
public enum ColliderKind : byte
{
    /// <summary>
    /// No collision shape.
    /// </summary>
    None = 0,

    /// <summary>
    /// Circular boundary collider.
    /// </summary>
    Circle = 1,

    /// <summary>
    /// Axis-Aligned Bounding Box (AABB) collider.
    /// </summary>
    Aabb = 2
}

/// <summary>
/// Represents physical collision bounds and collision filtering properties for an entity.
/// Unmanaged struct component.
/// </summary>
/// <param name="Kind">The collider primitive shape.</param>
/// <param name="Radius">Radius when Kind is Circle.</param>
/// <param name="Box">Bounding box dimensions when Kind is AABB.</param>
/// <param name="IsTrigger">Whether collision triggers events without physical displacement resolution.</param>
/// <param name="Layer">Collision layer bitmask.</param>
/// <param name="Mask">Collision target mask bitmask.</param>
public record struct Collider2D(
    ColliderKind Kind,
    float Radius = 0f,
    AABB2D Box = default,
    bool IsTrigger = false,
    uint Layer = 1,
    uint Mask = uint.MaxValue)
{
    /// <summary>
    /// Creates a circular collider component.
    /// </summary>
    /// <param name="radius">Circle radius.</param>
    /// <param name="isTrigger">Whether trigger-only.</param>
    /// <returns>A new <see cref="Collider2D"/> component.</returns>
    public static Collider2D CreateCircle(float radius, bool isTrigger = false) =>
        new(ColliderKind.Circle, Radius: radius, IsTrigger: isTrigger);

    /// <summary>
    /// Creates an AABB collider component.
    /// </summary>
    /// <param name="box">AABB bounds.</param>
    /// <param name="isTrigger">Whether trigger-only.</param>
    /// <returns>A new <see cref="Collider2D"/> component.</returns>
    public static Collider2D CreateAabb(in AABB2D box, bool isTrigger = false) =>
        new(ColliderKind.Aabb, Box: box, IsTrigger: isTrigger);
}
