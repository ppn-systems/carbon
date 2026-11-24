// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Maps.Biomes;

namespace Carbon.Maps.Building;

/// <summary>
/// Result status of a building placement evaluation.
/// </summary>
public enum PlacementValidationResult : byte
{
    /// <summary>
    /// Placement is valid and passes all foundation, terrain, and clearance criteria.
    /// </summary>
    Success = 0,

    /// <summary>
    /// Terrain load-bearing capacity is insufficient for the requested structure weight.
    /// </summary>
    InsufficientAffordance = 1,

    /// <summary>
    /// Placement is blocked by open water (ocean, river) without a bridge foundation.
    /// </summary>
    BlockedByWater = 2,

    /// <summary>
    /// Tile is currently obstructed by a solid natural prop (tree trunk, mineral rock) that must be cleared first.
    /// </summary>
    BlockedByNaturalObstacle = 3,

    /// <summary>
    /// Another constructed structure already occupies this tile.
    /// </summary>
    BlockedByExistingStructure = 4
}

/// <summary>
/// Domain service evaluating terrain affordance and structural placement validity.
/// Governs foundation rules, terrain bearing capacity, and environmental obstacle clearance.
/// Zero heap allocations.
/// </summary>
public static class BuildingFoundation
{
    /// <summary>
    /// Resolves the terrain load-bearing affordance provided by a biome type.
    /// </summary>
    /// <param name="biome">The biome type of the terrain tile.</param>
    /// <returns>The affordance rating.</returns>
    public static TerrainAffordance GetAffordance(BiomeType biome) => biome switch
    {
        BiomeType.DeepOcean or BiomeType.ShallowWater => TerrainAffordance.None,
        BiomeType.DuneDesert or BiomeType.MistySwamp or BiomeType.BeachSand => TerrainAffordance.Medium,
        BiomeType.LushPlains or BiomeType.AncientForest or BiomeType.RedMesa or
        BiomeType.TropicalJungle or BiomeType.FrozenTundra or BiomeType.VolcanicAshland => TerrainAffordance.Heavy,
        _ => TerrainAffordance.Heavy
    };

    /// <summary>
    /// Resolves the minimum terrain affordance required to construct a structure type.
    /// </summary>
    /// <param name="type">The blueprint structure type.</param>
    /// <returns>Minimum affordance rating required.</returns>
    public static TerrainAffordance GetRequiredAffordance(StructureType type) => type switch
    {
        StructureType.None => TerrainAffordance.None,
        StructureType.WoodFloor or StructureType.StoneFloor => TerrainAffordance.Light,
        StructureType.WoodWall or StructureType.WoodDoor => TerrainAffordance.Medium,
        StructureType.StoneWall or StructureType.StoneDoor or StructureType.RoofPillar => TerrainAffordance.Heavy,
        _ => TerrainAffordance.Heavy
    };

    /// <summary>
    /// Evaluates whether a structure blueprint can be successfully placed on a tile.
    /// </summary>
    /// <param name="affordance">The terrain affordance of the tile.</param>
    /// <param name="type">The structure blueprint type to construct.</param>
    /// <param name="hasSolidNaturalObstacle">Whether a solid natural prop (trunk, ore) occupies this tile.</param>
    /// <param name="hasExistingStructure">Whether another constructed structure already occupies this tile.</param>
    /// <returns>The placement validation status.</returns>
    public static PlacementValidationResult ValidatePlacement(
        TerrainAffordance affordance,
        StructureType type,
        bool hasSolidNaturalObstacle,
        bool hasExistingStructure)
    {
        if (hasExistingStructure)
        {
            return PlacementValidationResult.BlockedByExistingStructure;
        }

        if (hasSolidNaturalObstacle)
        {
            return PlacementValidationResult.BlockedByNaturalObstacle;
        }

        if (affordance == TerrainAffordance.None)
        {
            return PlacementValidationResult.BlockedByWater;
        }

        TerrainAffordance required = GetRequiredAffordance(type);
        if (affordance < required)
        {
            return PlacementValidationResult.InsufficientAffordance;
        }

        return PlacementValidationResult.Success;
    }
}
