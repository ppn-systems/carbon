// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Maps.Biomes;

namespace Carbon.Maps.Building;

/// <summary>
/// High-performance spatial building grid managing constructed structures, smart wall autotiling,
/// destructibility lifecycle, and physical obstacle collision. Zero heap allocations during gameplay queries.
/// </summary>
public sealed class BuildingGrid : IBuildingGrid
{
    private readonly ConcurrentDictionary<(int X, int Y), StructureTile> _structures = new();

    /// <inheritdoc/>
    public int StructureCount => _structures.Count;

    /// <summary>
    /// Gets a snapshot enumerable of all placed structures and their coordinates.
    /// </summary>
    public IEnumerable<KeyValuePair<(int X, int Y), StructureTile>> PlacedStructures => _structures;

    /// <inheritdoc/>
    public bool TryGetStructure(int worldTileX, int worldTileY, out StructureTile structureTile) =>
        _structures.TryGetValue((worldTileX, worldTileY), out structureTile);

    /// <inheritdoc/>
    public bool IsSolidAt(int worldTileX, int worldTileY) =>
        _structures.TryGetValue((worldTileX, worldTileY), out StructureTile structure) && structure.IsSolid;

    /// <inheritdoc/>
    public bool IsRoofedAt(int worldTileX, int worldTileY)
    {
        if (_structures.TryGetValue((worldTileX, worldTileY), out StructureTile structure) && structure.HasRoof)
        {
            return true;
        }

        return RoomDetector.IsTileRoofed(worldTileX, worldTileY, this.SupportsRoofAt);
    }

    /// <summary>
    /// Evaluates whether a structure at (worldTileX, worldTileY) is actively capable of supporting a roof.
    /// </summary>
    public bool SupportsRoofAt(int worldTileX, int worldTileY) =>
        _structures.TryGetValue((worldTileX, worldTileY), out StructureTile structure) && structure.SupportsRoof;

    /// <inheritdoc/>
    public bool CanBuildAt(int worldTileX, int worldTileY, StructureType structureType, BiomeType biome, bool hasNaturalSolidProp)
    {
        TerrainAffordance affordance = BuildingFoundation.GetAffordance(biome);
        bool hasExistingStructure = _structures.ContainsKey((worldTileX, worldTileY));
        return BuildingFoundation.ValidatePlacement(affordance, structureType, hasNaturalSolidProp, hasExistingStructure) == PlacementValidationResult.Success;
    }

    /// <inheritdoc/>
    public PlacementValidationResult TryPlaceStructure(
        int worldTileX,
        int worldTileY,
        StructureType structureType,
        BiomeType biome,
        bool hasNaturalSolidProp,
        out StructureTile placedStructure)
    {
        TerrainAffordance affordance = BuildingFoundation.GetAffordance(biome);
        bool hasExistingStructure = _structures.ContainsKey((worldTileX, worldTileY));

        PlacementValidationResult result = BuildingFoundation.ValidatePlacement(affordance, structureType, hasNaturalSolidProp, hasExistingStructure);
        if (result != PlacementValidationResult.Success)
        {
            placedStructure = StructureTile.Empty;
            return result;
        }

        StructureTile blueprint = CreateBlueprint(structureType);

        if (WallAutotiler.CanConnectToWall(blueprint.Category))
        {
            WallConnectivityMask mask = WallAutotiler.ComputeConnectivity(worldTileX, worldTileY, this.IsConnectableAt);
            blueprint = blueprint.WithConnectivity(mask);
        }

        _structures[(worldTileX, worldTileY)] = blueprint;
        placedStructure = blueprint;

        // Automatically update the 4 cardinal neighbors so they snap to the new structure
        this.UpdateNeighborConnectivity(worldTileX, worldTileY);

        return PlacementValidationResult.Success;
    }

    /// <inheritdoc/>
    public bool TryDemolishStructure(int worldTileX, int worldTileY)
    {
        if (_structures.TryRemove((worldTileX, worldTileY), out _))
        {
            this.UpdateNeighborConnectivity(worldTileX, worldTileY);
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public bool ApplyDamage(int worldTileX, int worldTileY, float damageAmount, out StructureTile updatedStructure)
    {
        if (!_structures.TryGetValue((worldTileX, worldTileY), out StructureTile current))
        {
            updatedStructure = StructureTile.Empty;
            return false;
        }

        updatedStructure = current.ApplyDamage(damageAmount);
        _structures[(worldTileX, worldTileY)] = updatedStructure;

        if (updatedStructure.IsDestroyed)
        {
            // Structure collapsed; update adjacent walls so they disconnect from this rubble
            this.UpdateNeighborConnectivity(worldTileX, worldTileY);
        }

        return true;
    }

    /// <inheritdoc/>
    public bool ToggleDoor(int worldTileX, int worldTileY, out StructureTile updatedStructure)
    {
        if (!_structures.TryGetValue((worldTileX, worldTileY), out StructureTile current) || current.Category != StructureCategory.Door)
        {
            updatedStructure = StructureTile.Empty;
            return false;
        }

        DoorState nextState = current.DoorState is DoorState.Closed ? DoorState.Open : DoorState.Closed;
        updatedStructure = current.WithDoorState(nextState);
        _structures[(worldTileX, worldTileY)] = updatedStructure;
        return true;
    }

    /// <inheritdoc/>
    public bool ResolveCollision(ref Vector2 position, float radius, float tileSize, out Vector2 pushNormal)
    {
        pushNormal = Vector2.Zero;
        bool collided = false;

        int minTx = (int)MathF.Floor((position.X - radius) / tileSize);
        int maxTx = (int)MathF.Floor((position.X + radius) / tileSize);
        int minTy = (int)MathF.Floor((position.Y - radius) / tileSize);
        int maxTy = (int)MathF.Floor((position.Y + radius) / tileSize);

        for (int ty = minTy; ty <= maxTy; ty++)
        {
            for (int tx = minTx; tx <= maxTx; tx++)
            {
                if (!this.IsSolidAt(tx, ty))
                {
                    continue;
                }

                // Tile bounding box
                float boxMinX = tx * tileSize;
                float boxMinY = ty * tileSize;
                float boxMaxX = boxMinX + tileSize;
                float boxMaxY = boxMinY + tileSize;

                // Find closest point on AABB to circle center
                float closestX = Math.Clamp(position.X, boxMinX, boxMaxX);
                float closestY = Math.Clamp(position.Y, boxMinY, boxMaxY);

                Vector2 diff = position - new Vector2(closestX, closestY);
                float distSq = diff.LengthSquared();

                if (distSq < radius * radius)
                {
                    collided = true;
                    if (distSq > 0.0001f)
                    {
                        float dist = MathF.Sqrt(distSq);
                        Vector2 normal = diff / dist;
                        float penetration = radius - dist;
                        position += normal * penetration;
                        pushNormal += normal;
                    }
                    else
                    {
                        // Circle center is inside tile; push out horizontally
                        Vector2 normal = new(1f, 0f);
                        position += normal * radius;
                        pushNormal += normal;
                    }
                }
            }
        }

        if (collided && pushNormal.LengthSquared() > 0.0001f)
        {
            pushNormal = Vector2.Normalize(pushNormal);
        }

        return collided;
    }

    /// <summary>
    /// Clears all placed structures from the grid.
    /// </summary>
    public void Clear() => _structures.Clear();

    private bool IsConnectableAt(int x, int y) =>
        _structures.TryGetValue((x, y), out StructureTile neighbor) &&
        !neighbor.IsDestroyed &&
        WallAutotiler.CanConnectToWall(neighbor.Category);

    private void UpdateNeighborConnectivity(int centerX, int centerY)
    {
        Span<(int X, int Y)> neighbors =
        [
            (centerX, centerY - 1),
            (centerX + 1, centerY),
            (centerX, centerY + 1),
            (centerX - 1, centerY)
        ];

        for (int i = 0; i < neighbors.Length; i++)
        {
            (int nx, int ny) = neighbors[i];
            if (_structures.TryGetValue((nx, ny), out StructureTile neighbor) &&
                !neighbor.IsDestroyed &&
                WallAutotiler.CanConnectToWall(neighbor.Category))
            {
                WallConnectivityMask updatedMask = WallAutotiler.ComputeConnectivity(nx, ny, this.IsConnectableAt);
                _structures[(nx, ny)] = neighbor.WithConnectivity(updatedMask);
            }
        }
    }

    private static StructureTile CreateBlueprint(StructureType type) => type switch
    {
        StructureType.None => StructureTile.Empty,
        StructureType.WoodWall => StructureTile.CreateWall(type, StructureMaterial.Wood, durability: 150f, harvestTier: 1),
        StructureType.StoneWall => StructureTile.CreateWall(type, StructureMaterial.Stone, durability: 400f, harvestTier: 2),
        StructureType.WoodDoor => StructureTile.CreateDoor(type, StructureMaterial.Wood, durability: 100f, harvestTier: 1),
        StructureType.StoneDoor => StructureTile.CreateDoor(type, StructureMaterial.Stone, durability: 250f, harvestTier: 2),
        StructureType.WoodFloor => StructureTile.CreateFloor(type, StructureMaterial.Wood, moveSpeedMultiplier: 1.15f),
        StructureType.StoneFloor => StructureTile.CreateFloor(type, StructureMaterial.Stone, moveSpeedMultiplier: 1.20f),
        StructureType.RoofPillar => StructureTile.CreatePillar(StructureMaterial.Stone, durability: 300f),
        _ => StructureTile.Empty
    };
}
