// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Physics.Contacts;
using Carbon.Physics.Narrowphase;
using Carbon.Physics.Shapes;
using FluentAssertions;
using Xunit;

namespace Carbon.Physics.Tests;

public sealed class CollisionDetectorTests
{
    [Fact]
    public void TestCircleCircle_Overlap_CalculatesNormalAndDepth()
    {
        Circle2D a = new(new Vector2(0f, 0f), 10f);
        Circle2D b = new(new Vector2(15f, 0f), 10f);

        bool hit = CollisionDetector.TestCircleCircle(a, b, out CollisionContact contact);

        hit.Should().BeTrue();
        contact.HasCollision.Should().BeTrue();
        contact.Normal.X.Should().BeApproximately(1f, 0.001f);
        contact.Normal.Y.Should().Be(0f);
        contact.PenetrationDepth.Should().BeApproximately(5f, 0.001f);
    }

    [Fact]
    public void TestAabbAabb_Overlap_CalculatesNormalAndDepth()
    {
        AABB2D a = AABB2D.FromCenterSize(new Vector2(0f, 0f), new Vector2(20f, 20f));
        AABB2D b = AABB2D.FromCenterSize(new Vector2(16f, 0f), new Vector2(20f, 20f));

        bool hit = CollisionDetector.TestAabbAabb(a, b, out CollisionContact contact);

        hit.Should().BeTrue();
        contact.HasCollision.Should().BeTrue();
        contact.PenetrationDepth.Should().BeApproximately(4f, 0.001f);
    }

    [Fact]
    public void TestCircleAabb_Overlap_DetectsContact()
    {
        Circle2D circle = new(new Vector2(15f, 0f), 10f);
        AABB2D box = AABB2D.FromCenterSize(new Vector2(0f, 0f), new Vector2(20f, 20f)); // Extents X [-10..10]

        bool hit = CollisionDetector.TestCircleAabb(circle, box, out CollisionContact contact);

        hit.Should().BeTrue();
        contact.HasCollision.Should().BeTrue();
        contact.PenetrationDepth.Should().BeApproximately(5f, 0.001f);
    }
}
