// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Maps.Biomes;
using Carbon.Physics.Shapes;

namespace Carbon.Maps.Building;

/// <summary>
/// Read-only spatial grid interface for querying constructed structures, wall collision, and roof coverage.
/// Adheres to Interface Segregation Principle (ISP).
/// </summary>
public interface IReadOnlyBuildingGrid
{
    /// <summary>
    /// Gets the count of constructed structures registered on the grid.
    /// </summary>
    int StructureCount { get; }

    /// <summary>
    /// Attempts to retrieve the structure located at the specified world tile coordinates.
    /// </summary>
    bool TryGetStructure(int worldTileX, int worldTileY, out StructureTile structureTile);

    /// <summary>
    /// Evaluates whether the specified world tile coordinate contains an impassable physical solid structure.
    /// </summary>
    bool IsSolidAt(int worldTileX, int worldTileY);

    /// <summary>
    /// Evaluates whether the specified world tile coordinate is covered by an overhead constructed roof.
    /// </summary>
    bool IsRoofedAt(int worldTileX, int worldTileY);

    /// <summary>
    /// Evaluates whether a structure blueprint can be legally placed on the given tile.
    /// </summary>
    bool CanBuildAt(int worldTileX, int worldTileY, StructureType structureType, BiomeType biome, bool hasNaturalSolidProp);

    /// <summary>
    /// Resolves physical circle-box collision against solid constructed walls and closed doors.
    /// </summary>
    bool ResolveCollision(ref Vector2 position, float radius, float tileSize, out Vector2 pushNormal);

    /// <summary>
    /// Resolves physical AABB collision against solid constructed walls and closed doors.
    /// </summary>
    bool ResolveAabbCollision(ref Vector2 position, in AABB2D localBox, float tileSize, out Vector2 pushNormal);
}

/// <summary>
/// Mutable building grid interface for constructing, damaging, and interacting with structures.
/// </summary>
public interface IBuildingGrid : IReadOnlyBuildingGrid
{
    /// <summary>
    /// Attempts to construct a structure blueprint at the specified tile.
    /// Automatically updates connected wall shapes of adjacent neighbors.
    /// </summary>
    PlacementValidationResult TryPlaceStructure(
        int worldTileX,
        int worldTileY,
        StructureType structureType,
        BiomeType biome,
        bool hasNaturalSolidProp,
        out StructureTile placedStructure);

    /// <summary>
    /// Force-places a structure directly onto the grid without foundation validation (e.g. procedural generation / blueprints).
    /// </summary>
    void ForcePlaceStructure(int worldTileX, int worldTileY, StructureType structureType);

    /// <summary>
    /// Removes or demolishes the structure at the specified tile, updating neighbor connectivity.
    /// </summary>
    bool TryDemolishStructure(int worldTileX, int worldTileY);

    /// <summary>
    /// Inflicts durability damage upon the structure at the specified tile.
    /// If destroyed, collapses the structure and updates neighbor connectivity.
    /// </summary>
    bool ApplyDamage(int worldTileX, int worldTileY, float damageAmount, out StructureTile updatedStructure);

    /// <summary>
    /// Inflicts durability damage upon the structure at the specified tile if <paramref name="toolHarvestTier"/>
    /// meets or exceeds the structure's minimum harvest tier.
    /// </summary>
    bool ApplyDamage(int worldTileX, int worldTileY, float damageAmount, byte toolHarvestTier, out StructureTile updatedStructure);

    /// <summary>
    /// Toggles the operational state (Open / Closed) of a constructed door passage.
    /// </summary>
    bool ToggleDoor(int worldTileX, int worldTileY, out StructureTile updatedStructure);
}
