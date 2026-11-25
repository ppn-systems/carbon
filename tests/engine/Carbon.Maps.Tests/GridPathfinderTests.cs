// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Maps.Biomes;
using Carbon.Maps.Building;
using Carbon.Maps.Generators;
using Carbon.Maps.Navigation;
using Carbon.Maps.Streaming;
using Carbon.Physics.Raycast;
using Carbon.Physics.Shapes;
using Xunit;

namespace Carbon.Maps.Tests;

public sealed class GridPathfinderTests
{
    [Fact]
    public void FindPath_WithConstructedWallAndDoor_RoutesThroughOpenDoorAndDetoursWhenClosed()
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(42UL), tileSize: 32f);
        (int sx, int sy) = world.SpawnTile;

        // Build a vertical wall across x = sx + 2, with a door at y = sy
        for (int y = sy - 2; y <= sy + 2; y++)
        {
            StructureType type = y == sy ? StructureType.WoodDoor : StructureType.StoneWall;
            _ = world.Buildings.TryPlaceStructure(sx + 2, y, type, BiomeType.LushPlains, false, out _);
        }

        GridPathfinder pathfinder = new(maxNodes: 1024);
        Span<(int X, int Y)> buffer = stackalloc (int X, int Y)[128];

        // Door starts closed -> path must detour around the 5-tile wall
        PathSearchStatus closedStatus = pathfinder.FindPath(world.NavGrid, (sx, sy), (sx + 4, sy), buffer, out int closedLen);
        Assert.Equal(PathSearchStatus.Found, closedStatus);
        Assert.DoesNotContain((sx + 2, sy), buffer[..closedLen].ToArray());

        // Open the door and reuse the same pathfinder instance -> shorter straight path through the door
        Assert.True(world.Buildings.ToggleDoor(sx + 2, sy, out _));
        PathSearchStatus openStatus = pathfinder.FindPath(world.NavGrid, (sx, sy), (sx + 4, sy), buffer, out int openLen);
        Assert.Equal(PathSearchStatus.Found, openStatus);
        Assert.Contains((sx + 2, sy), buffer[..openLen].ToArray());
        Assert.True(openLen < closedLen);
    }

    [Fact]
    public void FindPath_BlockedGoalOrSearchLimit_ReturnsExpectedStatus()
    {
        StubWalkabilityGrid grid = new();
        grid.Blocked.Add((5, 5));

        GridPathfinder pathfinder = new(maxNodes: 64);
        Span<(int X, int Y)> buffer = stackalloc (int X, int Y)[32];

        PathSearchStatus blocked = pathfinder.FindPath(grid, (0, 0), (5, 5), buffer, out int len);
        Assert.Equal(PathSearchStatus.StartOrGoalBlocked, blocked);
        Assert.Equal(0, len);

        // Surround (0,0) with walls so (20,20) hits search/unreachable limit cleanly
        for (int i = -1; i <= 1; i++)
        {
            _ = grid.Blocked.Add((i, -1));
            _ = grid.Blocked.Add((i, 1));
            _ = grid.Blocked.Add((-1, i));
            _ = grid.Blocked.Add((1, i));
        }

        _ = grid.Blocked.Remove((0, 0));
        PathSearchStatus unreachable = pathfinder.FindPath(grid, (0, 0), (20, 20), buffer, out _);
        Assert.Equal(PathSearchStatus.Unreachable, unreachable);

        grid.Blocked.Clear();
        PathSearchStatus limited = pathfinder.FindPath(grid, (0, 0), (40, 40), buffer, out _, maxExpandedNodes: 4);
        Assert.Equal(PathSearchStatus.SearchLimitReached, limited);
    }

    [Fact]
    public void ChunkWorld2D_Raycast_HitsClosedDoorOrWallAndPassesOpenDoor()
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(42UL), tileSize: 32f);
        (int sx, int sy) = world.SpawnTile;

        Assert.Equal(
            PlacementValidationResult.Success,
            world.Buildings.TryPlaceStructure(sx + 2, sy, StructureType.WoodDoor, BiomeType.LushPlains, false, out _));

        Ray2D ray = new(new Vector2((sx + 0.5f) * 32f, (sy + 0.5f) * 32f), Vector2.UnitX, maxDistance: 128f);

        // Closed door blocks the ray at x = (sx + 2) * 32
        Assert.True(world.Raycast(in ray, out RaycastHit2D closedHit));
        Assert.Equal((sx + 2) * 32f, closedHit.Point.X, 0.05f);
        Assert.Equal(-1f, closedHit.Normal.X, 0.05f);

        // Open door lets the ray pass past x = (sx + 2) * 32
        Assert.True(world.Buildings.ToggleDoor(sx + 2, sy, out _));
        if (world.Raycast(in ray, out RaycastHit2D openHit))
        {
            Assert.True(openHit.Distance > closedHit.Distance + 1f);
        }
    }

    private sealed class StubWalkabilityGrid : ITileWalkabilityGrid
    {
        public System.Collections.Generic.HashSet<(int X, int Y)> Blocked { get; } = [];

        public TileWalkability GetWalkability(int worldTileX, int worldTileY) =>
            this.Blocked.Contains((worldTileX, worldTileY)) ? TileWalkability.Impassable : TileWalkability.WalkableNormal;

        public float GetMoveCost(int worldTileX, int worldTileY) =>
            this.IsWalkable(worldTileX, worldTileY) ? 1f : float.PositiveInfinity;

        public bool IsWalkable(int worldTileX, int worldTileY) =>
            !this.Blocked.Contains((worldTileX, worldTileY));

        public byte GetHeightLevel(int worldTileX, int worldTileY) => 0;

        public bool CanTraverse(int fromX, int fromY, int toX, int toY) =>
            this.IsWalkable(toX, toY);
    }
}
