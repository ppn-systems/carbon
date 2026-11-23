// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Physics.Shapes;
using FluentAssertions;
using Xunit;

namespace Carbon.Physics.Tests;

public sealed class Circle2DTests
{
    [Fact]
    public void OverlapsAndContains_ShouldEvaluateRadiusDistances()
    {
        Circle2D a = new(new Vector2(0f, 0f), 10f);
        Circle2D b = new(new Vector2(15f, 0f), 10f); // dist = 15 <= 20
        Circle2D c = new(new Vector2(30f, 0f), 10f); // dist = 30 > 20

        a.Overlaps(b).Should().BeTrue();
        a.Overlaps(c).Should().BeFalse();

        a.Contains(new Vector2(5f, 5f)).Should().BeTrue();
        a.Contains(new Vector2(12f, 0f)).Should().BeFalse();
    }
}
