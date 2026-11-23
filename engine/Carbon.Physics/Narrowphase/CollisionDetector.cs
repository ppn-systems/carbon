// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Physics.Contacts;
using Carbon.Physics.Shapes;

namespace Carbon.Physics.Narrowphase;

/// <summary>
/// High-performance narrowphase geometric collision detection algorithms.
/// Zero heap allocation.
/// </summary>
public static class CollisionDetector
{
    /// <summary>
    /// Tests collision between two 2D circles.
    /// </summary>
    /// <param name="a">First circle.</param>
    /// <param name="b">Second circle.</param>
    /// <param name="contact">Output contact information.</param>
    /// <returns><see langword="true"/> if colliding; otherwise, <see langword="false"/>.</returns>
    public static bool TestCircleCircle(in Circle2D a, in Circle2D b, out CollisionContact contact)
    {
        Vector2 delta = b.Center - a.Center;
        float distSq = delta.LengthSquared();
        float radiusSum = a.Radius + b.Radius;

        if (distSq > radiusSum * radiusSum)
        {
            contact = CollisionContact.None;
            return false;
        }

        float dist = MathF.Sqrt(distSq);
        Vector2 normal = dist > 0.0001f ? delta / dist : Vector2.UnitX;
        float depth = radiusSum - dist;

        contact = new CollisionContact(true, normal, depth);
        return true;
    }

    /// <summary>
    /// Tests collision between two 2D axis-aligned bounding boxes.
    /// </summary>
    /// <param name="a">First AABB.</param>
    /// <param name="b">Second AABB.</param>
    /// <param name="contact">Output contact information.</param>
    /// <returns><see langword="true"/> if colliding; otherwise, <see langword="false"/>.</returns>
    public static bool TestAabbAabb(in AABB2D a, in AABB2D b, out CollisionContact contact)
    {
        float dx1 = b.Max.X - a.Min.X;
        float dx2 = a.Max.X - b.Min.X;
        float overlapX = MathF.Min(dx1, dx2);

        if (overlapX <= 0f)
        {
            contact = CollisionContact.None;
            return false;
        }

        float dy1 = b.Max.Y - a.Min.Y;
        float dy2 = a.Max.Y - b.Min.Y;
        float overlapY = MathF.Min(dy1, dy2);

        if (overlapY <= 0f)
        {
            contact = CollisionContact.None;
            return false;
        }

        Vector2 normal;
        float depth;

        if (overlapX < overlapY)
        {
            depth = overlapX;
            normal = a.Center.X < b.Center.X ? Vector2.UnitX : -Vector2.UnitX;
        }
        else
        {
            depth = overlapY;
            normal = a.Center.Y < b.Center.Y ? Vector2.UnitY : -Vector2.UnitY;
        }

        contact = new CollisionContact(true, normal, depth);
        return true;
    }

    /// <summary>
    /// Tests collision between a circle and an AABB.
    /// </summary>
    /// <param name="circle">The circle.</param>
    /// <param name="aabb">The AABB.</param>
    /// <param name="contact">Output contact information.</param>
    /// <returns><see langword="true"/> if colliding; otherwise, <see langword="false"/>.</returns>
    public static bool TestCircleAabb(in Circle2D circle, in AABB2D aabb, out CollisionContact contact)
    {
        float closestX = Math.Clamp(circle.Center.X, aabb.Min.X, aabb.Max.X);
        float closestY = Math.Clamp(circle.Center.Y, aabb.Min.Y, aabb.Max.Y);
        Vector2 closest = new(closestX, closestY);

        Vector2 delta = circle.Center - closest;
        float distSq = delta.LengthSquared();

        if (distSq > circle.Radius * circle.Radius)
        {
            contact = CollisionContact.None;
            return false;
        }

        float dist = MathF.Sqrt(distSq);
        Vector2 normal = dist > 0.0001f ? delta / dist : Vector2.UnitX;
        float depth = circle.Radius - dist;

        contact = new CollisionContact(true, normal, depth);
        return true;
    }
}
