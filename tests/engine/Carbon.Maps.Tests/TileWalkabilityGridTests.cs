// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Maps.Biomes;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Navigation;
using Carbon.Maps.Props;
using Carbon.Maps.Streaming;
using Carbon.Maps.Topography;
using Xunit;

namespace Carbon.Maps.Tests;

public sealed class TileWalkabilityGridTests
{
    [Fact]
    public void TileWalkabilityGrid_ClassifiesNaturalTerrainAndWater()
    {
        ProceduralWorldGenerator generator = new(1337UL);
        ChunkWorld2D world = new(generator, tileSize: 32f);

        // Load chunks around origin
        world.UpdateAroundPosition(Vector2.Zero, chunkRadius: 1, evictionRadius: 3);
        Chunk2D chunk = world.GetOrCreateChunk(0, 0);

        // 1. Deep Ocean -> Impassable, infinite cost
        chunk.SetTile(0, 0, new MapTile(BiomeType.DeepOcean, 0.1f, 0.5f, 0.5f, true, 0));
        Assert.Equal(TileWalkability.Impassable, world.NavGrid.GetWalkability(0, 0));
        Assert.False(world.NavGrid.IsWalkable(0, 0));
        Assert.True(float.IsPositiveInfinity(world.NavGrid.GetMoveCost(0, 0)));

        // 2. Shallow Water -> WalkableSlow, higher cost
        chunk.SetTile(1, 0, new MapTile(BiomeType.ShallowWater, 0.3f, 0.5f, 0.5f, false, 0));
        Assert.Equal(TileWalkability.WalkableSlow, world.NavGrid.GetWalkability(1, 0));
        Assert.True(world.NavGrid.IsWalkable(1, 0));
        Assert.True(world.NavGrid.GetMoveCost(1, 0) > 1.2f);

        // 3. Normal Plains -> WalkableNormal, cost = 1.0f
        chunk.SetTile(2, 0, new MapTile(BiomeType.LushPlains, 0.5f, 0.5f, 0.5f, false, 0));
        Assert.Equal(TileWalkability.WalkableNormal, world.NavGrid.GetWalkability(2, 0));
        Assert.True(world.NavGrid.IsWalkable(2, 0));
        Assert.Equal(1.0f, world.NavGrid.GetMoveCost(2, 0));
    }

    [Fact]
    public void TileWalkabilityGrid_IntegratesNaturalPropsAndChoppingDestruction()
    {
        ProceduralWorldGenerator generator = new(1337UL);
        ChunkWorld2D world = new(generator, tileSize: 32f);

        world.UpdateAroundPosition(Vector2.Zero, chunkRadius: 1, evictionRadius: 3);
        Chunk2D chunk = world.GetOrCreateChunk(0, 0);

        chunk.SetTile(5, 5, new MapTile(BiomeType.LushPlains, 0.5f, 0.5f, 0.5f, false, 0));

        // Place an active oak tree at (5, 5)
        MapProp oakTree = MapProp.Create(
            type: MapPropType.OakTree,
            category: PropCategory.Flora,
            destructibility: PropDestructibility.Create(100f, 1),
            localX: 5,
            localY: 5,
            worldX: 5 * 32f,
            worldY: 5 * 32f,
            radius: 42f,
            collisionRadius: 8f,
            canopyRadius: 42f,
            canopyOffsetY: -22f,
            isSolid: true,
            colorPrimary: 0x166534FF,
            colorSecondary: 0x78350FFF);
        chunk.AddProp(oakTree);

        // Tree is alive -> Impassable!
        Assert.Equal(TileWalkability.Impassable, world.NavGrid.GetWalkability(5, 5));
        Assert.False(world.NavGrid.IsWalkable(5, 5));

        // Simulate chopping tree down (IsDestroyed = true)
        chunk.ClearProps();
        MapProp choppedTree = oakTree with
        {
            Destructibility = oakTree.Destructibility.ApplyDamage(100f)
        };
        chunk.AddProp(choppedTree);
        Assert.True(choppedTree.Destructibility.IsDestroyed);
        Assert.False(choppedTree.HasCollision);

        // Chopped tree -> Automatically becomes WalkableNormal!
        Assert.Equal(TileWalkability.WalkableNormal, world.NavGrid.GetWalkability(5, 5));
        Assert.True(world.NavGrid.IsWalkable(5, 5));
        Assert.Equal(1.0f, world.NavGrid.GetMoveCost(5, 5));
    }

    [Fact]
    public void TileWalkabilityGrid_IntegratesRimWorldBuildingsFloorsAndDoors()
    {
        ProceduralWorldGenerator generator = new(1337UL);
        ChunkWorld2D world = new(generator, tileSize: 32f);

        world.UpdateAroundPosition(Vector2.Zero, chunkRadius: 1, evictionRadius: 3);

        // 1. Solid stone wall at (10, 10) -> Impassable
        _ = world.Buildings.TryPlaceStructure(10, 10, StructureType.StoneWall, BiomeType.LushPlains, false, out _);
        Assert.Equal(TileWalkability.Impassable, world.NavGrid.GetWalkability(10, 10));
        Assert.False(world.NavGrid.IsWalkable(10, 10));

        // 2. Closed door at (10, 11) -> Impassable
        _ = world.Buildings.TryPlaceStructure(10, 11, StructureType.WoodDoor, BiomeType.LushPlains, false, out _);
        Assert.Equal(TileWalkability.Impassable, world.NavGrid.GetWalkability(10, 11));

        // Open the door -> Walkable
        _ = world.Buildings.ToggleDoor(10, 11, out _);
        Assert.True(world.NavGrid.IsWalkable(10, 11));

        // 3. Wood floor at (10, 12) -> WalkableFast with speed boost (cost < 1.0f)
        _ = world.Buildings.TryPlaceStructure(10, 12, StructureType.WoodFloor, BiomeType.LushPlains, false, out _);
        Assert.Equal(TileWalkability.WalkableFast, world.NavGrid.GetWalkability(10, 12));
        Assert.True(world.NavGrid.GetMoveCost(10, 12) < 0.9f);
    }

    [Fact]
    public void TileWalkabilityGrid_PreventsCornerCuttingThroughWalls()
    {
        ProceduralWorldGenerator generator = new(1337UL);
        ChunkWorld2D world = new(generator, tileSize: 32f);

        world.UpdateAroundPosition(Vector2.Zero, chunkRadius: 1, evictionRadius: 3);

        // Build a corner: Wall at (20, 19) and Wall at (19, 20)
        _ = world.Buildings.TryPlaceStructure(20, 19, StructureType.StoneWall, BiomeType.LushPlains, false, out _);
        _ = world.Buildings.TryPlaceStructure(19, 20, StructureType.StoneWall, BiomeType.LushPlains, false, out _);

        // Trying to walk diagonally from (19, 19) to (20, 20) through the squeeze point
        bool canSqueezeThroughCorner = world.NavGrid.CanTraverse(19, 19, 20, 20);

        // Must be blocked!
        Assert.False(canSqueezeThroughCorner);

        // But straight cardinal movement to an open tile is allowed
        Assert.True(world.NavGrid.CanTraverse(19, 19, 18, 19));
    }

    [Fact]
    public void TileWalkabilityGrid_RampAllowsCrossingPlateauCliffs()
    {
        ProceduralWorldGenerator generator = new(1337UL);
        ChunkWorld2D world = new(generator, tileSize: 32f);

        world.UpdateAroundPosition(Vector2.Zero, chunkRadius: 1, evictionRadius: 3);
        Chunk2D chunk = world.GetOrCreateChunk(0, 0);

        // Pick a lowland/plateau pair away from procedural ramp slots (their positions are jittered).
        int x = 0;
        int y = 1;
        while (TopographyHelper.IsPotentialRampSlot(x, y) ||
               TopographyHelper.IsPotentialRampSlot(x, y - 1) ||
               world.HasSolidPropAt(x, y) ||
               world.HasSolidPropAt(x, y - 1))
        {
            x++;
            if (x >= Chunk2D.Size)
            {
                x = 0;
                y++;
            }
        }

        // Tile (x, y) is Level 0 (Lowland)
        chunk.SetTile(x, y, new MapTile(BiomeType.LushPlains, 0.4f, 0.5f, 0.5f, false, 0, HeightLevel: 0));

        // Tile (x, y - 1) is Level 1 (Plateau)
        chunk.SetTile(x, y - 1, new MapTile(BiomeType.LushPlains, 0.72f, 0.5f, 0.5f, false, 0, HeightLevel: 1));

        // Without ramp -> Cannot traverse from Level 0 to Level 1
        Assert.False(world.NavGrid.CanTraverse(x, y, x, y - 1));

        // Add North ramp at (x, y)
        world.NavGrid.SetRamp(x, y, RampDirection.North);
        Assert.Equal(TileWalkability.WalkableRamp, world.NavGrid.GetWalkability(x, y));

        // With ramp -> Can traverse smoothly across the height levels!
        Assert.True(world.NavGrid.CanTraverse(x, y, x, y - 1));
        Assert.True(world.NavGrid.CanTraverse(x, y - 1, x, y));
    }
}
