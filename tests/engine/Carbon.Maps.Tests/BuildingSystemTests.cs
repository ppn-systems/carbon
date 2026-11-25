// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Maps.Biomes;
using Carbon.Maps.Building;
using Carbon.Maps.Generators;
using Carbon.Maps.Streaming;
using Xunit;

namespace Carbon.Maps.Tests;

public sealed class BuildingSystemTests
{
    [Fact]
    public void BuildingFoundation_EvaluatesAffordanceAndClearance()
    {
        // 1. Water bodies have zero load-bearing affordance
        Assert.Equal(TerrainAffordance.None, BuildingFoundation.GetAffordance(BiomeType.DeepOcean));
        Assert.Equal(TerrainAffordance.None, BuildingFoundation.GetAffordance(BiomeType.ShallowWater));

        // 2. Sand/swamp biomes have medium affordance
        Assert.Equal(TerrainAffordance.Medium, BuildingFoundation.GetAffordance(BiomeType.DuneDesert));
        Assert.Equal(TerrainAffordance.Medium, BuildingFoundation.GetAffordance(BiomeType.MistySwamp));

        // 3. Solid land biomes have heavy affordance
        Assert.Equal(TerrainAffordance.Heavy, BuildingFoundation.GetAffordance(BiomeType.LushPlains));
        Assert.Equal(TerrainAffordance.Heavy, BuildingFoundation.GetAffordance(BiomeType.AncientForest));

        // 4. WoodWall requires Medium, StoneWall requires Heavy
        Assert.Equal(TerrainAffordance.Medium, BuildingFoundation.GetRequiredAffordance(StructureType.WoodWall));
        Assert.Equal(TerrainAffordance.Heavy, BuildingFoundation.GetRequiredAffordance(StructureType.StoneWall));

        // Test placement in DeepOcean -> BlockedByWater
        PlacementValidationResult oceanResult = BuildingFoundation.ValidatePlacement(
            TerrainAffordance.None,
            StructureType.WoodWall,
            hasSolidNaturalObstacle: false,
            hasExistingStructure: false);
        Assert.Equal(PlacementValidationResult.BlockedByWater, oceanResult);

        // Test StoneWall on Medium sand terrain -> InsufficientAffordance
        PlacementValidationResult desertStoneResult = BuildingFoundation.ValidatePlacement(
            TerrainAffordance.Medium,
            StructureType.StoneWall,
            hasSolidNaturalObstacle: false,
            hasExistingStructure: false);
        Assert.Equal(PlacementValidationResult.InsufficientAffordance, desertStoneResult);

        // Test WoodWall on Medium sand terrain -> Success
        PlacementValidationResult desertWoodResult = BuildingFoundation.ValidatePlacement(
            TerrainAffordance.Medium,
            StructureType.WoodWall,
            hasSolidNaturalObstacle: false,
            hasExistingStructure: false);
        Assert.Equal(PlacementValidationResult.Success, desertWoodResult);

        // Test tile obstructed by tree trunk -> BlockedByNaturalObstacle
        PlacementValidationResult trunkBlocked = BuildingFoundation.ValidatePlacement(
            TerrainAffordance.Heavy,
            StructureType.StoneWall,
            hasSolidNaturalObstacle: true,
            hasExistingStructure: false);
        Assert.Equal(PlacementValidationResult.BlockedByNaturalObstacle, trunkBlocked);

        // Test tile already occupied by another structure -> BlockedByExistingStructure
        PlacementValidationResult existingBlocked = BuildingFoundation.ValidatePlacement(
            TerrainAffordance.Heavy,
            StructureType.StoneWall,
            hasSolidNaturalObstacle: false,
            hasExistingStructure: true);
        Assert.Equal(PlacementValidationResult.BlockedByExistingStructure, existingBlocked);
    }

    [Fact]
    public void WallAutotiler_Computes16CardinalCornerShapes()
    {
        // Isolated
        Assert.Equal(WallConnectionShape.Isolated, WallAutotiler.ToShape(WallConnectivityMask.None));

        // End caps
        Assert.Equal(WallConnectionShape.EndNorth, WallAutotiler.ToShape(WallConnectivityMask.North));
        Assert.Equal(WallConnectionShape.EndEast, WallAutotiler.ToShape(WallConnectivityMask.East));
        Assert.Equal(WallConnectionShape.EndSouth, WallAutotiler.ToShape(WallConnectivityMask.South));
        Assert.Equal(WallConnectionShape.EndWest, WallAutotiler.ToShape(WallConnectivityMask.West));

        // Straight corridors
        Assert.Equal(WallConnectionShape.StraightVertical, WallAutotiler.ToShape(WallConnectivityMask.North | WallConnectivityMask.South));
        Assert.Equal(WallConnectionShape.StraightHorizontal, WallAutotiler.ToShape(WallConnectivityMask.East | WallConnectivityMask.West));

        // Corners (L-junctions)
        Assert.Equal(WallConnectionShape.CornerNorthEast, WallAutotiler.ToShape(WallConnectivityMask.North | WallConnectivityMask.East));
        Assert.Equal(WallConnectionShape.CornerEastSouth, WallAutotiler.ToShape(WallConnectivityMask.East | WallConnectivityMask.South));
        Assert.Equal(WallConnectionShape.CornerSouthWest, WallAutotiler.ToShape(WallConnectivityMask.South | WallConnectivityMask.West));
        Assert.Equal(WallConnectionShape.CornerWestNorth, WallAutotiler.ToShape(WallConnectivityMask.West | WallConnectivityMask.North));

        // T-junctions
        Assert.Equal(WallConnectionShape.TJunctionNES, WallAutotiler.ToShape(WallConnectivityMask.North | WallConnectivityMask.East | WallConnectivityMask.South));
        Assert.Equal(WallConnectionShape.TJunctionESW, WallAutotiler.ToShape(WallConnectivityMask.East | WallConnectivityMask.South | WallConnectivityMask.West));
        Assert.Equal(WallConnectionShape.TJunctionSWN, WallAutotiler.ToShape(WallConnectivityMask.South | WallConnectivityMask.West | WallConnectivityMask.North));
        Assert.Equal(WallConnectionShape.TJunctionWNE, WallAutotiler.ToShape(WallConnectivityMask.West | WallConnectivityMask.North | WallConnectivityMask.East));

        // 4-way crossroad
        Assert.Equal(
            WallConnectionShape.CrossIntersection,
            WallAutotiler.ToShape(WallConnectivityMask.North | WallConnectivityMask.East | WallConnectivityMask.South | WallConnectivityMask.West));
    }

    [Fact]
    public void BuildingGrid_PlacesWallsAndUpdatesNeighborConnectivity()
    {
        BuildingGrid grid = new();

        // 1. Place central wall at (10, 10)
        PlacementValidationResult res1 = grid.TryPlaceStructure(10, 10, StructureType.WoodWall, BiomeType.LushPlains, false, out StructureTile center);
        Assert.Equal(PlacementValidationResult.Success, res1);
        Assert.Equal(WallConnectionShape.Isolated, center.ConnectionShape);

        // 2. Place South neighbor at (10, 11) -> Center becomes EndSouth
        _ = grid.TryPlaceStructure(10, 11, StructureType.WoodWall, BiomeType.LushPlains, false, out StructureTile south);
        Assert.Equal(WallConnectionShape.EndNorth, south.ConnectionShape);

        Assert.True(grid.TryGetStructure(10, 10, out center));
        Assert.Equal(WallConnectionShape.EndSouth, center.ConnectionShape);

        // 3. Place East neighbor at (11, 10) -> Center becomes CornerEastSouth
        _ = grid.TryPlaceStructure(11, 10, StructureType.WoodWall, BiomeType.LushPlains, false, out _);
        Assert.True(grid.TryGetStructure(10, 10, out center));
        Assert.Equal(WallConnectionShape.CornerEastSouth, center.ConnectionShape);

        // 4. Place North neighbor at (10, 9) -> Center becomes TJunctionNES
        _ = grid.TryPlaceStructure(10, 9, StructureType.WoodWall, BiomeType.LushPlains, false, out _);
        Assert.True(grid.TryGetStructure(10, 10, out center));
        Assert.Equal(WallConnectionShape.TJunctionNES, center.ConnectionShape);

        // 5. Place West neighbor at (9, 10) -> Center becomes CrossIntersection
        _ = grid.TryPlaceStructure(9, 10, StructureType.WoodWall, BiomeType.LushPlains, false, out _);
        Assert.True(grid.TryGetStructure(10, 10, out center));
        Assert.Equal(WallConnectionShape.CrossIntersection, center.ConnectionShape);

        // 6. Demolish West neighbor -> Center reverts to TJunctionNES
        bool demolished = grid.TryDemolishStructure(9, 10);
        Assert.True(demolished);
        Assert.True(grid.TryGetStructure(10, 10, out center));
        Assert.Equal(WallConnectionShape.TJunctionNES, center.ConnectionShape);
    }

    [Fact]
    public void BuildingGrid_ApplyDamage_DestroysStructureAndUpdatesConnectivity()
    {
        BuildingGrid grid = new();

        // Place two adjacent walls at (5, 5) and (5, 6)
        _ = grid.TryPlaceStructure(5, 5, StructureType.WoodWall, BiomeType.LushPlains, false, out _);
        _ = grid.TryPlaceStructure(5, 6, StructureType.WoodWall, BiomeType.LushPlains, false, out StructureTile wall2);

        Assert.Equal(150f, wall2.CurrentDurability);
        Assert.False(wall2.IsDestroyed);

        // Inflict partial damage (50 HP)
        bool damaged = grid.ApplyDamage(5, 6, 50f, out StructureTile damagedWall);
        Assert.True(damaged);
        Assert.Equal(100f, damagedWall.CurrentDurability);
        Assert.False(damagedWall.IsDestroyed);
        Assert.True(damagedWall.IsSolid);

        // Inflict lethal damage (120 HP)
        bool destroyed = grid.ApplyDamage(5, 6, 120f, out StructureTile destroyedWall);
        Assert.True(destroyed);
        Assert.Equal(0f, destroyedWall.CurrentDurability);
        Assert.True(destroyedWall.IsDestroyed);
        Assert.False(destroyedWall.IsSolid); // Destroyed wall no longer blocks movement

        // Adjacent wall at (5, 5) automatically disconnects from the collapsed wall
        Assert.True(grid.TryGetStructure(5, 5, out StructureTile wall1));
        Assert.Equal(WallConnectionShape.Isolated, wall1.ConnectionShape);
    }

    [Fact]
    public void BuildingGrid_ToggleDoor_ChangesPassageCollision()
    {
        BuildingGrid grid = new();

        _ = grid.TryPlaceStructure(20, 20, StructureType.WoodDoor, BiomeType.LushPlains, false, out StructureTile door);
        Assert.Equal(DoorState.Closed, door.DoorState);
        Assert.True(grid.IsSolidAt(20, 20));

        // Open the door
        bool toggledOpen = grid.ToggleDoor(20, 20, out StructureTile openDoor);
        Assert.True(toggledOpen);
        Assert.Equal(DoorState.Open, openDoor.DoorState);
        Assert.False(grid.IsSolidAt(20, 20)); // Passable!

        // Close the door again
        bool toggledClosed = grid.ToggleDoor(20, 20, out StructureTile closedDoor);
        Assert.True(toggledClosed);
        Assert.Equal(DoorState.Closed, closedDoor.DoorState);
        Assert.True(grid.IsSolidAt(20, 20)); // Impassable again!
    }

    [Fact]
    public void RoomDetector_IdentifiesEnclosedRoomAndRoofSupport()
    {
        BuildingGrid grid = new();

        // Build a 4x4 wall perimeter (interior is a 2x2 room at (1, 1), (1, 2), (2, 1), (2, 2))
        // Perimeter walls:
        // (0,0) (1,0) (2,0) (3,0)
        // (0,1)             (3,1)
        // (0,2)             (3,2)
        // (0,3) (1,3) (2,3) (3,3)
        for (int x = 0; x <= 3; x++)
        {
            _ = grid.TryPlaceStructure(x, 0, StructureType.StoneWall, BiomeType.LushPlains, false, out _);
            _ = grid.TryPlaceStructure(x, 3, StructureType.StoneWall, BiomeType.LushPlains, false, out _);
        }

        for (int y = 1; y <= 2; y++)
        {
            _ = grid.TryPlaceStructure(0, y, StructureType.StoneWall, BiomeType.LushPlains, false, out _);
            _ = grid.TryPlaceStructure(3, y, StructureType.StoneWall, BiomeType.LushPlains, false, out _);
        }

        // Test room detection from interior point (1, 1)
        RoomEnclosure room = RoomDetector.DetectRoom(1, 1, grid.IsSolidAt, maxRoomTiles: 50);

        Assert.True(room.IsEnclosed);
        Assert.Equal(4, room.TileCount); // 2x2 interior = 4 tiles
        Assert.True(room.ContainsTile(1, 1));
        Assert.True(room.ContainsTile(2, 2));
        Assert.False(room.ContainsTile(0, 0)); // Wall itself is not an interior floor

        // All interior tiles are within 6 tiles of walls, so they are roofed
        Assert.True(RoomDetector.IsTileRoofed(1, 1, grid.SupportsRoofAt));
        Assert.True(grid.IsRoofedAt(1, 1));

        // Test unroofed / outdoors: wilderness starting point far away
        RoomEnclosure outdoors = RoomDetector.DetectRoom(50, 50, grid.IsSolidAt, maxRoomTiles: 20);
        Assert.False(outdoors.IsEnclosed);
    }

    [Fact]
    public void ChunkWorld2D_ResolveCollision_BlocksPlayerAgainstConstructedWalls()
    {
        ProceduralWorldGenerator generator = new(1337UL);
        ChunkWorld2D world = new(generator, tileSize: 32f);

        // Load chunks around origin
        world.UpdateAroundPosition(Vector2.Zero, chunkRadius: 1, evictionRadius: 3);

        // Place a solid stone wall at tile (4, 4) -> world box [128..160, 128..160]
        PlacementValidationResult res = world.Buildings.TryPlaceStructure(4, 4, StructureType.StoneWall, BiomeType.LushPlains, false, out _);
        Assert.Equal(PlacementValidationResult.Success, res);

        // Player with radius 10 approaching wall from the left at (120, 144) (collides with box at x=128)
        Vector2 playerPos = new(125f, 144f);
        bool collided = world.ResolveEntityCollision(ref playerPos, radius: 10f, out Vector2 pushNormal);

        Assert.True(collided);
        // Player should be pushed out to the left (X <= 118f)
        Assert.True(playerPos.X <= 118.01f);
        Assert.True(pushNormal.X < -0.5f);
    }

    [Fact]
    public void IsRoofedAt_IsolatedOutdoorWall_DoesNotRoofWilderness_WhileEnclosedRoomWithOpenDoorIsRoofed()
    {
        BuildingGrid grid = new();
        _ = grid.TryPlaceStructure(50, 50, StructureType.StoneWall, BiomeType.LushPlains, false, out _);

        Assert.False(grid.IsRoofedAt(50, 50));
        Assert.False(grid.IsRoofedAt(51, 50));
        Assert.False(grid.IsRoofedAt(49, 50));

        // 4x4 room around (1..2, 1..2) with a WoodDoor at (1, 0) toggled Open
        for (int x = 0; x <= 3; x++)
        {
            StructureType topType = x == 1 ? StructureType.WoodDoor : StructureType.StoneWall;
            _ = grid.TryPlaceStructure(x, 0, topType, BiomeType.LushPlains, false, out _);
            _ = grid.TryPlaceStructure(x, 3, StructureType.StoneWall, BiomeType.LushPlains, false, out _);
        }

        for (int y = 1; y <= 2; y++)
        {
            _ = grid.TryPlaceStructure(0, y, StructureType.StoneWall, BiomeType.LushPlains, false, out _);
            _ = grid.TryPlaceStructure(3, y, StructureType.StoneWall, BiomeType.LushPlains, false, out _);
        }

        Assert.True(grid.ToggleDoor(1, 0, out _));
        Assert.True(grid.IsRoofedAt(1, 1));
        Assert.True(grid.IsRoofedAt(2, 2));
        Assert.True(grid.IsRoofedAt(1, 0)); // Open doorway stays roofed
        Assert.False(grid.IsRoofedAt(-1, 1)); // Exterior outside wall stays unroofed

        // Breaching a wall opens the room to sky and invalidates the cached roof
        Assert.True(grid.TryDemolishStructure(2, 0));
        Assert.False(grid.IsRoofedAt(1, 1));
    }

    [Fact]
    public void HarvestTier_PreventsLowTierToolsFromDamagingHighTierWallsAndProps()
    {
        BuildingGrid grid = new();
        _ = grid.TryPlaceStructure(2, 2, StructureType.StoneWall, BiomeType.LushPlains, false, out StructureTile placed);
        Assert.Equal(2, placed.Destructibility.HarvestTier);

        Assert.False(grid.ApplyDamage(2, 2, 100f, toolHarvestTier: 1, out StructureTile tier1Result));
        Assert.Equal(400f, tier1Result.CurrentDurability);

        Assert.True(grid.ApplyDamage(2, 2, 100f, toolHarvestTier: 2, out StructureTile tier2Result));
        Assert.Equal(300f, tier2Result.CurrentDurability);
    }

    [Fact]
    public void ChunkWorld2D_ResolveAabbCollision_BlocksWallAndAllowsOpenDoor()
    {
        ProceduralWorldGenerator generator = new(1337UL);
        ChunkWorld2D world = new(generator, tileSize: 32f);
        world.UpdateAroundPosition(Vector2.Zero, chunkRadius: 1, evictionRadius: 3);

        Assert.Equal(PlacementValidationResult.Success, world.Buildings.TryPlaceStructure(4, 4, StructureType.StoneWall, BiomeType.LushPlains, false, out _));
        Assert.Equal(PlacementValidationResult.Success, world.Buildings.TryPlaceStructure(4, 5, StructureType.WoodDoor, BiomeType.LushPlains, false, out _));
        Assert.True(world.Buildings.ToggleDoor(4, 5, out _));

        Carbon.Physics.Shapes.AABB2D box = Carbon.Physics.Shapes.AABB2D.FromCenterSize(Vector2.Zero, new Vector2(16f, 16f));
        Vector2 againstWall = new(124f, 144f);
        Assert.True(world.ResolveAabbCollision(ref againstWall, in box, out Vector2 wallNormal));
        Assert.True(againstWall.X <= 120.01f);
        Assert.True(wallNormal.X < -0.5f);

        Vector2 throughDoor = new(144f, 176f);
        Assert.False(world.ResolveAabbCollision(ref throughDoor, in box, out _));
    }
}
