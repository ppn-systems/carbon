// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Physics.Separation;
using FluentAssertions;
using Xunit;

namespace Carbon.Physics.Tests;

public sealed class EntitySeparationTests
{
    [Fact]
    public void ResolveCircleOverlap_PushesEntitiesApartTangentially()
    {
        Vector2 posA = new(0f, 0f);
        Vector2 posB = new(14f, 0f);
        float radiusA = 10f;
        float radiusB = 10f; // Overlap = 20 - 14 = 6. With 50/50 split, each moves 3 units.

        bool resolved = EntitySeparation.ResolveCircleOverlap(ref posA, ref posB, radiusA, radiusB, separationRatio: 0.5f);

        resolved.Should().BeTrue();
        posA.X.Should().BeApproximately(-3f, 0.001f);
        posB.X.Should().BeApproximately(17f, 0.001f);

        float newDistance = Vector2.Distance(posA, posB);
        newDistance.Should().BeApproximately(20f, 0.001f);
    }
}
