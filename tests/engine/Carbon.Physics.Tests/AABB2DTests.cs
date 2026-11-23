// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Physics.Shapes;
using FluentAssertions;
using Xunit;

namespace Carbon.Physics.Tests;

public sealed class AABB2DTests
{
    [Fact]
    public void FromCenterSize_ShouldCalculateMinAndMaxCorrectly()
    {
        AABB2D box = AABB2D.FromCenterSize(new Vector2(100f, 100f), new Vector2(40f, 20f));

        box.Min.Should().Be(new Vector2(80f, 90f));
        box.Max.Should().Be(new Vector2(120f, 110f));
        box.Center.Should().Be(new Vector2(100f, 100f));
        box.Extents.Should().Be(new Vector2(20f, 10f));
        box.Width.Should().Be(40f);
        box.Height.Should().Be(20f);
    }

    [Fact]
    public void OverlapsAndContains_ShouldEvaluateBounds()
    {
        AABB2D a = new(new Vector2(0f, 0f), new Vector2(50f, 50f));
        AABB2D b = new(new Vector2(40f, 40f), new Vector2(80f, 80f));
        AABB2D c = new(new Vector2(60f, 60f), new Vector2(90f, 90f));

        a.Overlaps(b).Should().BeTrue();
        a.Overlaps(c).Should().BeFalse();

        a.Contains(new Vector2(25f, 25f)).Should().BeTrue();
        a.Contains(new Vector2(55f, 25f)).Should().BeFalse();
    }
}
