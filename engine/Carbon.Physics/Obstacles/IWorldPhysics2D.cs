// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Physics.Raycast;
using Carbon.Physics.Shapes;

namespace Carbon.Physics.Obstacles;

/// <summary>
/// Abstraction for querying world surface movement speed modifiers, resolving continuous circle-vs-world collisions,
/// and casting rays against static world geometry.
/// </summary>
public interface IWorldPhysics2D
{
    /// <summary>
    /// Gets the movement speed multiplier at the specified world position.
    /// </summary>
    /// <param name="worldPosition">World position in world units.</param>
    /// <returns>The terrain or floor movement speed multiplier (1.0 for normal terrain).</returns>
    float GetMoveSpeedMultiplier(Vector2 worldPosition);

    /// <summary>
    /// Resolves circular entity collision against static world obstacles, modifying <paramref name="position"/> in-place for smooth sliding.
    /// </summary>
    /// <param name="position">Entity position in world coordinates.</param>
    /// <param name="radius">Entity collision radius.</param>
    /// <param name="pushNormal">Normalized pushout surface normal if a collision occurred; otherwise, <see cref="Vector2.Zero"/>.</param>
    /// <returns><see langword="true"/> if a collision was resolved; otherwise, <see langword="false"/>.</returns>
    bool ResolveCircleCollision(ref Vector2 position, float radius, out Vector2 pushNormal);

    /// <summary>
    /// Resolves axis-aligned bounding box entity collision against static world obstacles, modifying <paramref name="position"/> in-place.
    /// </summary>
    /// <param name="position">Entity world origin position.</param>
    /// <param name="localBox">Entity local-space bounding box relative to <paramref name="position"/>.</param>
    /// <param name="pushNormal">Normalized pushout surface normal if a collision occurred; otherwise, <see cref="Vector2.Zero"/>.</param>
    /// <returns><see langword="true"/> if a collision was resolved; otherwise, <see langword="false"/>.</returns>
    bool ResolveAabbCollision(ref Vector2 position, in AABB2D localBox, out Vector2 pushNormal);

    /// <summary>
    /// Casts a ray against static world obstacles, returning the earliest hit.
    /// </summary>
    /// <param name="ray">The 2D testing ray.</param>
    /// <param name="hit">Output hit information if an obstacle was intersected.</param>
    /// <returns><see langword="true"/> if an obstacle was hit; otherwise, <see langword="false"/>.</returns>
    bool Raycast(in Ray2D ray, out RaycastHit2D hit);
}
