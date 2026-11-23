// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Graphics.Batching.Camera;
using Carbon.Graphics.Batching.Common;
using FluentAssertions;
using Xunit;

namespace Carbon.Graphics.Batching.Tests;

public sealed class FrustumCulling2DTests
{
    [Fact]
    public void IsVisible_WithAABB_ShouldDetectIntersectionsCorrectly()
    {
        RectangleF camera = new(0f, 0f, 800f, 600f);

        // Completely inside
        RectangleF inside = new(100f, 100f, 50f, 50f);
        FrustumCulling2D.IsVisible(camera, inside).Should().BeTrue();

        // Overlapping boundary
        RectangleF overlapping = new(780f, 550f, 100f, 100f);
        FrustumCulling2D.IsVisible(camera, overlapping).Should().BeTrue();

        // Completely outside to the right
        RectangleF outsideRight = new(850f, 100f, 50f, 50f);
        FrustumCulling2D.IsVisible(camera, outsideRight).Should().BeFalse();

        // Completely outside above
        RectangleF outsideAbove = new(100f, -100f, 50f, 50f);
        FrustumCulling2D.IsVisible(camera, outsideAbove).Should().BeFalse();
    }

    [Fact]
    public void IsVisible_WithPositionAndSize_ShouldDetectIntersectionsCorrectly()
    {
        RectangleF camera = new(100f, 100f, 400f, 300f);

        Vector2 posInside = new(200f, 200f);
        Vector2 size = new(32f, 32f);
        FrustumCulling2D.IsVisible(camera, posInside, size).Should().BeTrue();

        Vector2 posOutside = new(0f, 0f);
        FrustumCulling2D.IsVisible(camera, posOutside, size).Should().BeFalse();
    }

    [Fact]
    public void IsVisible_WithRadialCircle_ShouldDetectCircleIntersections()
    {
        RectangleF camera = new(100f, 100f, 400f, 300f); // X: 100..500, Y: 100..400

        // Center inside camera
        FrustumCulling2D.IsVisible(camera, new Vector2(200f, 200f), radius: 10f).Should().BeTrue();

        // Center outside but radius touches left edge (center X=80, radius=25 => reaches X=105)
        FrustumCulling2D.IsVisible(camera, new Vector2(80f, 200f), radius: 25f).Should().BeTrue();

        // Center outside and radius cannot reach (center X=50, radius=20 => reaches X=70, edge is 100)
        FrustumCulling2D.IsVisible(camera, new Vector2(50f, 200f), radius: 20f).Should().BeFalse();

        // Diagonal corner check (corner at 100, 100; center at 80, 80; dist = sqrt(400+400) = ~28.28)
        FrustumCulling2D.IsVisible(camera, new Vector2(80f, 80f), radius: 30f).Should().BeTrue();
        FrustumCulling2D.IsVisible(camera, new Vector2(80f, 80f), radius: 25f).Should().BeFalse();
    }
}
