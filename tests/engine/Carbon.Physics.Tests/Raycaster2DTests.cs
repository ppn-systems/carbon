// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Physics.Raycast;
using Carbon.Physics.Shapes;
using Xunit;

namespace Carbon.Physics.Tests;

public sealed class Raycaster2DTests
{
    [Fact]
    public void IntersectCircle_DirectHit_ReturnsTrueAndCorrectPoint()
    {
        Ray2D ray = new(new Vector2(0f, 0f), new Vector2(1f, 0f), 100f);
        Circle2D circle = new(new Vector2(10f, 0f), 2f);

        bool hitResult = Raycaster2D.IntersectCircle(ray, circle, out RaycastHit2D hit);

        Assert.True(hitResult);
        Assert.True(hit.HasHit);
        Assert.Equal(8f, hit.Distance, 3);
        Assert.Equal(new Vector2(8f, 0f), hit.Point);
        Assert.Equal(new Vector2(-1f, 0f), hit.Normal);
    }

    [Fact]
    public void IntersectCircle_RayMiss_ReturnsFalse()
    {
        Ray2D ray = new(new Vector2(0f, 0f), new Vector2(1f, 0f), 100f);
        Circle2D circle = new(new Vector2(10f, 5f), 2f);

        bool hitResult = Raycaster2D.IntersectCircle(ray, circle, out RaycastHit2D hit);

        Assert.False(hitResult);
        Assert.False(hit.HasHit);
    }

    [Fact]
    public void IntersectCircle_BeyondMaxDistance_ReturnsFalse()
    {
        Ray2D ray = new(new Vector2(0f, 0f), new Vector2(1f, 0f), 5f);
        Circle2D circle = new(new Vector2(10f, 0f), 2f);

        bool hitResult = Raycaster2D.IntersectCircle(ray, circle, out RaycastHit2D hit);

        Assert.False(hitResult);
        Assert.False(hit.HasHit);
    }

    [Fact]
    public void IntersectAABB_DirectHit_ReturnsTrueAndCorrectNormal()
    {
        Ray2D ray = new(new Vector2(0f, 5f), new Vector2(1f, 0f), 50f);
        AABB2D box = new(new Vector2(10f, 0f), new Vector2(20f, 10f)); // X: [10, 20], Y: [0, 10]

        bool hitResult = Raycaster2D.IntersectAABB(ray, box, out RaycastHit2D hit);

        Assert.True(hitResult);
        Assert.True(hit.HasHit);
        Assert.Equal(10f, hit.Distance, 3);
        Assert.Equal(new Vector2(10f, 5f), hit.Point);
        Assert.Equal(new Vector2(-1f, 0f), hit.Normal);
    }

    [Fact]
    public void IntersectAABB_RayMiss_ReturnsFalse()
    {
        Ray2D ray = new(new Vector2(0f, 20f), new Vector2(1f, 0f), 50f);
        AABB2D box = new(new Vector2(10f, 0f), new Vector2(20f, 10f));

        bool hitResult = Raycaster2D.IntersectAABB(ray, box, out RaycastHit2D hit);

        Assert.False(hitResult);
        Assert.False(hit.HasHit);
    }
}
