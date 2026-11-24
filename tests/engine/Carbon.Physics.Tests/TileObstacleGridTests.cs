// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Physics.Obstacles;
using Carbon.Physics.Raycast;
using Carbon.Physics.Shapes;
using Xunit;

namespace Carbon.Physics.Tests;

public sealed class TileObstacleGridTests
{
    [Fact]
    public void TileObstacleGrid_SetSolid_RetrievesAccurately()
    {
        TileObstacleGrid grid = new(10, 10, 32f);

        Assert.False(grid.IsSolid(2, 3));
        grid.SetSolid(2, 3, true);
        Assert.True(grid.IsSolid(2, 3));

        // Out of bounds is treated as solid boundary
        Assert.True(grid.IsSolid(-1, 0));
        Assert.True(grid.IsSolid(10, 5));
    }

    [Fact]
    public void ResolveCircle_SolidWall_PushesCircleAway()
    {
        // Grid 10x10 with tile size 32. Tile (1, 0) spans X:[32, 64], Y:[0, 32]
        TileObstacleGrid grid = new(10, 10, 32f);
        grid.SetSolid(1, 0, true);

        // Circle at (31, 16) with radius 5 penetrates the left face of tile (1,0) at X=32
        Vector2 position = new(31f, 16f);
        float radius = 5f;

        bool resolved = grid.ResolveCircle(ref position, radius);

        Assert.True(resolved);
        // Circle must be pushed to X <= 27 (32 - 5)
        Assert.True(position.X <= 27.01f);
    }

    [Fact]
    public void Raycast_ThroughSolidTile_DetectsObstacle()
    {
        TileObstacleGrid grid = new(10, 10, 32f);
        grid.SetSolid(3, 0, true); // X: [96, 128], Y: [0, 32]

        Ray2D ray = new(new Vector2(0f, 16f), new Vector2(1f, 0f), 200f);

        bool hitResult = grid.Raycast(ray, out RaycastHit2D hit);

        Assert.True(hitResult);
        Assert.True(hit.HasHit);
        Assert.Equal(96f, hit.Distance, 3);
        Assert.Equal(new Vector2(96f, 16f), hit.Point);
        Assert.Equal(new Vector2(-1f, 0f), hit.Normal);
    }
}
