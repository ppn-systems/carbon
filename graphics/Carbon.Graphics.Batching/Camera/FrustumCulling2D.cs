// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Batching.Common;

namespace Carbon.Graphics.Batching.Camera;

/// <summary>
/// Provides fast 2D frustum culling tests to discard entities and map chunks outside the camera's visible bounds.
/// </summary>
public static class FrustumCulling2D
{
    /// <summary>
    /// Determines whether an axis-aligned bounding box intersects the camera view.
    /// </summary>
    /// <param name="cameraBounds">The visible world bounds of the camera.</param>
    /// <param name="objectBounds">The world bounds of the object to test.</param>
    /// <returns><see langword="true"/> if the object is visible; otherwise, <see langword="false"/>.</returns>
    public static bool IsVisible(in RectangleF cameraBounds, in RectangleF objectBounds) =>
        cameraBounds.Intersects(objectBounds);

    /// <summary>
    /// Determines whether a rectangular object defined by position and size is visible within the camera view.
    /// </summary>
    /// <param name="cameraBounds">The visible world bounds of the camera.</param>
    /// <param name="position">The top-left position of the object.</param>
    /// <param name="size">The width and height of the object.</param>
    /// <returns><see langword="true"/> if visible; otherwise, <see langword="false"/>.</returns>
    public static bool IsVisible(in RectangleF cameraBounds, in Vector2 position, in Vector2 size)
    {
        RectangleF objectBounds = new(position.X, position.Y, size.X, size.Y);
        return cameraBounds.Intersects(objectBounds);
    }

    /// <summary>
    /// Determines whether a circular area (e.g. radial effect or light) intersects the camera view.
    /// </summary>
    /// <param name="cameraBounds">The visible world bounds of the camera.</param>
    /// <param name="center">The circle center in world space.</param>
    /// <param name="radius">The radius of the circle.</param>
    /// <returns><see langword="true"/> if visible; otherwise, <see langword="false"/>.</returns>
    public static bool IsVisible(in RectangleF cameraBounds, in Vector2 center, float radius)
    {
        float closestX = Math.Clamp(center.X, cameraBounds.Left, cameraBounds.Right);
        float closestY = Math.Clamp(center.Y, cameraBounds.Top, cameraBounds.Bottom);

        float distanceX = center.X - closestX;
        float distanceY = center.Y - closestY;

        return ((distanceX * distanceX) + (distanceY * distanceY)) <= (radius * radius);
    }
}
