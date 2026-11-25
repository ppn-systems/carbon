// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Physics.Obstacles;
using Carbon.Physics.Separation;
using Carbon.Physics.Shapes;
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

    [Fact]
    public void ResolveStaticAabb_AndVsCircle_SeparateAlongMinimumPenetration()
    {
        AABB2D box = AABB2D.FromCenterSize(Vector2.Zero, new Vector2(10f, 10f));
        AABB2D obstacle = new(new Vector2(3f, -20f), new Vector2(20f, 20f));
        Vector2 pos = Vector2.Zero;
        Vector2 normal = Vector2.Zero;

        EntitySeparation.ResolveStaticAabb(ref pos, in box, in obstacle, ref normal).Should().BeTrue();
        pos.X.Should().BeApproximately(-2f, 0.001f);
        normal.X.Should().BeLessThan(0f);

        Vector2 posVsCircle = new(0f, 0f);
        Vector2 circleNormal = Vector2.Zero;
        EntitySeparation.ResolveStaticAabbVsCircle(ref posVsCircle, in box, new Vector2(8f, 0f), circleRadius: 5f, ref circleNormal).Should().BeTrue();
        posVsCircle.X.Should().BeApproximately(-2f, 0.001f);
        circleNormal.X.Should().BeLessThan(0f);
    }

    [Fact]
    public void TileObstacleGrid_ResolveAabbCollision_PushesBoxClearOfSolidTile()
    {
        TileObstacleGrid grid = new(width: 8, height: 8, tileSize: 32f);
        grid.SetSolid(2, 1, true); // [64..96, 32..64]

        AABB2D localBox = AABB2D.FromCenterSize(Vector2.Zero, new Vector2(16f, 16f));
        Vector2 pos = new(60f, 48f); // Max.X = 68 penetrates x=64 by 4 units

        grid.ResolveAabbCollision(ref pos, in localBox, out Vector2 pushNormal).Should().BeTrue();
        pos.X.Should().BeApproximately(56f, 0.01f);
        pushNormal.X.Should().BeLessThan(-0.5f);
    }
}
