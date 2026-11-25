// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Physics.Contacts;
using Carbon.Physics.Narrowphase;
using Carbon.Physics.Shapes;

namespace Carbon.Physics.Separation;

/// <summary>
/// Provides soft and hard separation force resolution so entities do not stack or overlap on top of each other.
/// Ideal for MMORPG entity bumping, crowd separation, and path obstruction.
/// Zero heap allocation.
/// </summary>
public static class EntitySeparation
{
    /// <summary>
    /// Resolves an overlap between two circular entity colliders by separating their positions.
    /// </summary>
    /// <param name="posA">Reference to position of entity A (mutated on collision).</param>
    /// <param name="posB">Reference to position of entity B (mutated on collision).</param>
    /// <param name="radiusA">Radius of entity A collider.</param>
    /// <param name="radiusB">Radius of entity B collider.</param>
    /// <param name="separationRatio">Separation weight [0.0 = only B moves, 0.5 = equal push, 1.0 = only A moves].</param>
    /// <returns><see langword="true"/> if an overlap was resolved; otherwise, <see langword="false"/>.</returns>
    public static bool ResolveCircleOverlap(
        ref Vector2 posA,
        ref Vector2 posB,
        float radiusA,
        float radiusB,
        float separationRatio = 0.5f)
    {
        Circle2D a = new(posA, radiusA);
        Circle2D b = new(posB, radiusB);

        if (CollisionDetector.TestCircleCircle(a, b, out CollisionContact contact) && contact.HasCollision)
        {
            Vector2 displacement = contact.Normal * contact.PenetrationDepth;
            posA -= displacement * (1f - separationRatio);
            posB += displacement * separationRatio;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Resolves an overlap between two AABB entity colliders by separating their positions.
    /// </summary>
    /// <param name="posA">Reference to position of entity A (mutated on collision).</param>
    /// <param name="localBoxA">Local-space AABB bounds of entity A.</param>
    /// <param name="posB">Reference to position of entity B (mutated on collision).</param>
    /// <param name="localBoxB">Local-space AABB bounds of entity B.</param>
    /// <param name="separationRatio">Separation weight [0.0 = only A moves, 0.5 = equal push, 1.0 = only B moves].</param>
    /// <returns><see langword="true"/> if an overlap was resolved; otherwise, <see langword="false"/>.</returns>
    public static bool ResolveAabbOverlap(
        ref Vector2 posA,
        in AABB2D localBoxA,
        ref Vector2 posB,
        in AABB2D localBoxB,
        float separationRatio = 0.5f)
    {
        AABB2D boxA = localBoxA.Offset(posA);
        AABB2D boxB = localBoxB.Offset(posB);

        if (CollisionDetector.TestAabbAabb(in boxA, in boxB, out CollisionContact contact) && contact.HasCollision)
        {
            Vector2 displacement = contact.Normal * contact.PenetrationDepth;
            posA -= displacement * (1f - separationRatio);
            posB += displacement * separationRatio;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Resolves an overlap between a circular collider and an AABB collider by separating their positions.
    /// </summary>
    /// <param name="circlePos">Reference to position of the circle entity (mutated on collision).</param>
    /// <param name="radius">Radius of the circle entity.</param>
    /// <param name="boxPos">Reference to position of the AABB entity (mutated on collision).</param>
    /// <param name="localBox">Local-space AABB bounds relative to <paramref name="boxPos"/>.</param>
    /// <param name="separationRatio">Separation weight applied to the box [0.0 = only circle moves, 0.5 = equal push, 1.0 = only box moves].</param>
    /// <returns><see langword="true"/> if an overlap was resolved; otherwise, <see langword="false"/>.</returns>
    public static bool ResolveCircleAabbOverlap(
        ref Vector2 circlePos,
        float radius,
        ref Vector2 boxPos,
        in AABB2D localBox,
        float separationRatio = 0.5f)
    {
        AABB2D worldBox = localBox.Offset(boxPos);
        Circle2D circle = new(circlePos, radius);

        if (!CollisionDetector.TestCircleAabb(in circle, in worldBox, out CollisionContact contact) || !contact.HasCollision)
        {
            return false;
        }

        Vector2 push;
        if (worldBox.Contains(circlePos))
        {
            float left = circlePos.X - worldBox.Min.X;
            float right = worldBox.Max.X - circlePos.X;
            float top = circlePos.Y - worldBox.Min.Y;
            float bottom = worldBox.Max.Y - circlePos.Y;
            float minDist = MathF.Min(MathF.Min(left, right), MathF.Min(top, bottom));

            Vector2 outNormal = minDist == left
                ? -Vector2.UnitX
                : minDist == right
                    ? Vector2.UnitX
                    : minDist == top
                        ? -Vector2.UnitY
                        : Vector2.UnitY;
            push = outNormal * (minDist + radius);
        }
        else
        {
            // TestCircleAabb normal points from AABB toward the circle center.
            push = contact.Normal * contact.PenetrationDepth;
        }

        circlePos += push * (1f - separationRatio);
        boxPos -= push * separationRatio;
        return true;
    }

    /// <summary>
    /// Resolves an entity's local <see cref="AABB2D"/> against a static world-space <paramref name="obstacleBox"/>,
    /// pushing <paramref name="entityPosition"/> outward along the minimum penetration axis.
    /// </summary>
    /// <param name="entityPosition">World origin position of the moving entity.</param>
    /// <param name="localBox">Local-space bounds relative to <paramref name="entityPosition"/>.</param>
    /// <param name="obstacleBox">Static world-space obstacle bounding box.</param>
    /// <param name="accumulatedNormal">Running push normal accumulator updated when separation occurs.</param>
    /// <returns><see langword="true"/> if separation occurred; otherwise, <see langword="false"/>.</returns>
    public static bool ResolveStaticAabb(
        ref Vector2 entityPosition,
        in AABB2D localBox,
        in AABB2D obstacleBox,
        ref Vector2 accumulatedNormal)
    {
        AABB2D entityBox = localBox.Offset(entityPosition);
        float overlapX = MathF.Min(entityBox.Max.X, obstacleBox.Max.X) - MathF.Max(entityBox.Min.X, obstacleBox.Min.X);
        float overlapY = MathF.Min(entityBox.Max.Y, obstacleBox.Max.Y) - MathF.Max(entityBox.Min.Y, obstacleBox.Min.Y);

        if (overlapX <= 1e-5f || overlapY <= 1e-5f)
        {
            return false;
        }

        Vector2 entityCenter = entityBox.Center;
        Vector2 obstacleCenter = obstacleBox.Center;
        Vector2 normal;
        float penetration;

        if (overlapX <= overlapY)
        {
            normal = entityCenter.X < obstacleCenter.X ? -Vector2.UnitX : Vector2.UnitX;
            penetration = overlapX;
        }
        else
        {
            normal = entityCenter.Y < obstacleCenter.Y ? -Vector2.UnitY : Vector2.UnitY;
            penetration = overlapY;
        }

        entityPosition += normal * penetration;
        accumulatedNormal += normal;
        return true;
    }

    /// <summary>
    /// Resolves an entity's local <see cref="AABB2D"/> against a static circular obstacle,
    /// pushing <paramref name="entityPosition"/> outward from the circle.
    /// </summary>
    /// <param name="entityPosition">World origin position of the moving entity.</param>
    /// <param name="localBox">Local-space bounds relative to <paramref name="entityPosition"/>.</param>
    /// <param name="circleCenter">Center of the static circle obstacle.</param>
    /// <param name="circleRadius">Radius of the static circle obstacle.</param>
    /// <param name="accumulatedNormal">Running push normal accumulator updated when separation occurs.</param>
    /// <returns><see langword="true"/> if separation occurred; otherwise, <see langword="false"/>.</returns>
    public static bool ResolveStaticAabbVsCircle(
        ref Vector2 entityPosition,
        in AABB2D localBox,
        Vector2 circleCenter,
        float circleRadius,
        ref Vector2 accumulatedNormal)
    {
        AABB2D entityBox = localBox.Offset(entityPosition);
        Vector2 closest = Vector2.Clamp(circleCenter, entityBox.Min, entityBox.Max);
        Vector2 diff = closest - circleCenter;
        float distSq = diff.LengthSquared();

        if (distSq >= circleRadius * circleRadius)
        {
            return false;
        }

        Vector2 normal;
        float penetration;
        if (distSq > 1e-6f)
        {
            float dist = MathF.Sqrt(distSq);
            normal = diff / dist;
            penetration = circleRadius - dist;
        }
        else
        {
            float dLeft = MathF.Abs(circleCenter.X - entityBox.Min.X);
            float dRight = MathF.Abs(entityBox.Max.X - circleCenter.X);
            float dBottom = MathF.Abs(circleCenter.Y - entityBox.Min.Y);
            float dTop = MathF.Abs(entityBox.Max.Y - circleCenter.Y);
            float minD = MathF.Min(MathF.Min(dLeft, dRight), MathF.Min(dBottom, dTop));
            normal = minD == dLeft
                ? Vector2.UnitX
                : minD == dRight
                    ? -Vector2.UnitX
                    : minD == dBottom
                        ? Vector2.UnitY
                        : -Vector2.UnitY;
            penetration = circleRadius + minD;
        }

        entityPosition += normal * penetration;
        accumulatedNormal += normal;
        return true;
    }
}
