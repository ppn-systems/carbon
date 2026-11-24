// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Physics.Shapes;

namespace Carbon.Physics.Raycast;

/// <summary>
/// High-performance, zero-allocation 2D geometric raycasting operations against geometric shapes.
/// </summary>
public static class Raycaster2D
{
    private const float Epsilon = 1e-6f;

    /// <summary>
    /// Performs a raycast test between a ray and a 2D circle.
    /// </summary>
    /// <param name="ray">The 2D testing ray.</param>
    /// <param name="circle">The target circle shape.</param>
    /// <param name="hit">Output hit information if intersection occurs.</param>
    /// <returns><see langword="true"/> if the ray hits the circle; otherwise, <see langword="false"/>.</returns>
    public static bool IntersectCircle(in Ray2D ray, in Circle2D circle, out RaycastHit2D hit)
    {
        Vector2 m = ray.Origin - circle.Center;
        float b = Vector2.Dot(m, ray.Direction);
        float c = Vector2.Dot(m, m) - (circle.Radius * circle.Radius);

        // Ray origin is outside the circle and points away from the circle
        if (c > 0f && b > 0f)
        {
            hit = RaycastHit2D.None;
            return false;
        }

        float discriminant = (b * b) - c;
        if (discriminant < 0f)
        {
            hit = RaycastHit2D.None;
            return false;
        }

        float sqrtDisc = MathF.Sqrt(discriminant);
        float t = -b - sqrtDisc;

        // If t < 0, ray started inside circle; clamp contact to origin
        if (t < 0f)
        {
            t = 0f;
        }

        if (t > ray.MaxDistance)
        {
            hit = RaycastHit2D.None;
            return false;
        }

        Vector2 point = ray.GetPoint(t);
        Vector2 diff = point - circle.Center;
        float diffLenSq = diff.LengthSquared();
        Vector2 normal = diffLenSq > Epsilon ? Vector2.Normalize(diff) : -ray.Direction;

        hit = new RaycastHit2D(true, t, point, normal);
        return true;
    }

    /// <summary>
    /// Performs a raycast test between a ray and an axis-aligned bounding box (AABB2D) using the slab method.
    /// </summary>
    /// <param name="ray">The 2D testing ray.</param>
    /// <param name="box">The target AABB shape.</param>
    /// <param name="hit">Output hit information if intersection occurs.</param>
    /// <returns><see langword="true"/> if the ray hits the AABB; otherwise, <see langword="false"/>.</returns>
    public static bool IntersectAABB(in Ray2D ray, in AABB2D box, out RaycastHit2D hit)
    {
        float tNear = float.NegativeInfinity;
        float tFar = float.PositiveInfinity;
        Vector2 hitNormal = Vector2.Zero;

        Vector2 boxMin = box.Min;
        Vector2 boxMax = box.Max;

        // X-axis slab
        if (MathF.Abs(ray.Direction.X) < Epsilon)
        {
            if (ray.Origin.X < boxMin.X || ray.Origin.X > boxMax.X)
            {
                hit = RaycastHit2D.None;
                return false;
            }
        }
        else
        {
            float invD = 1f / ray.Direction.X;
            float t1 = (boxMin.X - ray.Origin.X) * invD;
            float t2 = (boxMax.X - ray.Origin.X) * invD;
            Vector2 normal1 = -Vector2.UnitX;

            if (t1 > t2)
            {
                (t1, t2) = (t2, t1);
                normal1 = Vector2.UnitX;
            }

            if (t1 > tNear)
            {
                tNear = t1;
                hitNormal = normal1;
            }

            tFar = MathF.Min(tFar, t2);

            if (tNear > tFar || tFar < 0f)
            {
                hit = RaycastHit2D.None;
                return false;
            }
        }

        // Y-axis slab
        if (MathF.Abs(ray.Direction.Y) < Epsilon)
        {
            if (ray.Origin.Y < boxMin.Y || ray.Origin.Y > boxMax.Y)
            {
                hit = RaycastHit2D.None;
                return false;
            }
        }
        else
        {
            float invD = 1f / ray.Direction.Y;
            float t1 = (boxMin.Y - ray.Origin.Y) * invD;
            float t2 = (boxMax.Y - ray.Origin.Y) * invD;
            Vector2 normal1 = -Vector2.UnitY;

            if (t1 > t2)
            {
                (t1, t2) = (t2, t1);
                normal1 = Vector2.UnitY;
            }

            if (t1 > tNear)
            {
                tNear = t1;
                hitNormal = normal1;
            }

            tFar = MathF.Min(tFar, t2);

            if (tNear > tFar || tFar < 0f)
            {
                hit = RaycastHit2D.None;
                return false;
            }
        }

        float t = tNear < 0f ? 0f : tNear;
        if (t > ray.MaxDistance)
        {
            hit = RaycastHit2D.None;
            return false;
        }

        Vector2 point = ray.GetPoint(t);
        hit = new RaycastHit2D(true, t, point, hitNormal);
        return true;
    }
}
