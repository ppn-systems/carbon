// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Physics.Separation;
using Carbon.Physics.Shapes;

namespace Carbon.Maps.Building;

/// <summary>
/// High-performance spatial building grid managing constructed structures, smart wall autotiling,
/// destructibility lifecycle, and physical obstacle collision. Zero heap allocations during gameplay queries.
/// </summary>
public sealed class BuildingGrid : IBuildingGrid
{
    private readonly ConcurrentDictionary<(int X, int Y), StructureTile> _structures = new();
    private readonly ConcurrentDictionary<(int X, int Y), bool> _roofCache = new();
    private readonly Func<int, int, bool> _isConnectableDelegate;
    private readonly Func<int, int, bool> _supportsRoofDelegate;
    private readonly Func<int, int, bool> _isRoomBoundaryDelegate;
    private readonly Action<int, int, StructureTile>? _onStructureChanged;
    private readonly Action<int, int, WallConnectivityMask>? _onConnectivityUpdated;
    private int _revision;

    /// <summary>
    /// Initializes a new standalone instance of the <see cref="BuildingGrid"/> class.
    /// </summary>
    public BuildingGrid()
    {
        _isConnectableDelegate = this.IsConnectableAt;
        _supportsRoofDelegate = this.SupportsRoofAt;
        _isRoomBoundaryDelegate = this.IsRoomBoundaryAt;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BuildingGrid"/> class wired to delta synchronization callbacks.
    /// </summary>
    internal BuildingGrid(
        Action<int, int, StructureTile>? onStructureChanged,
        Action<int, int, WallConnectivityMask>? onConnectivityUpdated = null)
        : this()
    {
        _onStructureChanged = onStructureChanged;
        _onConnectivityUpdated = onConnectivityUpdated;
    }

    /// <inheritdoc/>
    public int StructureCount => _structures.Count;

    /// <summary>
    /// Gets a counter bumped by every change to the set or shape of structures (placement, removal, destruction, door
    /// toggles, chunk imports). Lets readers such as the roof renderer cache room detection.
    /// </summary>
    public int Revision => Volatile.Read(ref _revision);

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
        if (_roofCache.TryGetValue((worldTileX, worldTileY), out bool cached))
        {
            return cached;
        }

        bool roofed = this.ComputeIsRoofedAt(worldTileX, worldTileY);
        _roofCache[(worldTileX, worldTileY)] = roofed;
        return roofed;
    }

    /// <summary>
    /// Evaluates whether a structure at (worldTileX, worldTileY) forms a room perimeter boundary (intact wall or door).
    /// </summary>
    public bool IsRoomBoundaryAt(int worldTileX, int worldTileY) =>
        _structures.TryGetValue((worldTileX, worldTileY), out StructureTile structure) &&
        !structure.IsDestroyed &&
        (structure.Category is StructureCategory.Wall or StructureCategory.Door);

    private bool ComputeIsRoofedAt(int worldTileX, int worldTileY)
    {
        if (_structures.TryGetValue((worldTileX, worldTileY), out StructureTile structure) && !structure.IsDestroyed)
        {
            if (structure.HasRoof)
            {
                return true;
            }

            if (structure.Category is StructureCategory.Wall or StructureCategory.Door)
            {
                return this.IsAdjacentInteriorRoofed(worldTileX, worldTileY);
            }
        }

        return this.ComputeInteriorTileRoofed(worldTileX, worldTileY);
    }

    private bool IsAdjacentInteriorRoofed(int boundaryX, int boundaryY)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                {
                    continue;
                }

                int nx = boundaryX + dx;
                int ny = boundaryY + dy;
                if (!this.IsRoomBoundaryAt(nx, ny) && this.ComputeInteriorTileRoofed(nx, ny))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool ComputeInteriorTileRoofed(int worldTileX, int worldTileY) =>
        RoomDetector.IsTileRoofed(worldTileX, worldTileY, _supportsRoofDelegate) &&
        RoomDetector.IsTileEnclosed(worldTileX, worldTileY, _isRoomBoundaryDelegate);

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
            WallConnectivityMask mask = WallAutotiler.ComputeConnectivity(worldTileX, worldTileY, _isConnectableDelegate);
            blueprint = blueprint.WithConnectivity(mask);
        }

        _structures[(worldTileX, worldTileY)] = blueprint;
        placedStructure = blueprint;
        _roofCache.Clear();
        _ = Interlocked.Increment(ref _revision);

        // Automatically update the 4 cardinal neighbors so they snap to the new structure
        this.UpdateNeighborConnectivity(worldTileX, worldTileY);
        _onStructureChanged?.Invoke(worldTileX, worldTileY, blueprint);

        return PlacementValidationResult.Success;
    }

    /// <inheritdoc/>
    public void ForcePlaceStructure(int worldTileX, int worldTileY, StructureType structureType)
    {
        StructureTile blueprint = CreateBlueprint(structureType);

        if (WallAutotiler.CanConnectToWall(blueprint.Category))
        {
            WallConnectivityMask mask = WallAutotiler.ComputeConnectivity(worldTileX, worldTileY, _isConnectableDelegate);
            blueprint = blueprint.WithConnectivity(mask);
        }

        _structures[(worldTileX, worldTileY)] = blueprint;
        _roofCache.Clear();
        _ = Interlocked.Increment(ref _revision);
        this.UpdateNeighborConnectivity(worldTileX, worldTileY);
        _onStructureChanged?.Invoke(worldTileX, worldTileY, blueprint);
    }

    /// <summary>
    /// Places a fully specified structure tile (e.g. a completed construction built from a data-driven definition)
    /// without foundation validation, recomputing wall autotiling for the tile and its neighbors.
    /// </summary>
    /// <param name="worldTileX">World tile X.</param>
    /// <param name="worldTileY">World tile Y.</param>
    /// <param name="structure">The structure to place; an empty tile is ignored.</param>
    public void PlaceStructure(int worldTileX, int worldTileY, in StructureTile structure)
    {
        if (!structure.IsOccupied)
        {
            return;
        }

        StructureTile placed = structure;
        if (WallAutotiler.CanConnectToWall(placed.Category))
        {
            placed = placed.WithConnectivity(WallAutotiler.ComputeConnectivity(worldTileX, worldTileY, _isConnectableDelegate));
        }

        _structures[(worldTileX, worldTileY)] = placed;
        _roofCache.Clear();
        _ = Interlocked.Increment(ref _revision);
        this.UpdateNeighborConnectivity(worldTileX, worldTileY);
        _onStructureChanged?.Invoke(worldTileX, worldTileY, placed);
    }

    /// <inheritdoc/>
    public bool TryDemolishStructure(int worldTileX, int worldTileY)
    {
        if (_structures.TryRemove((worldTileX, worldTileY), out _))
        {
            _roofCache.Clear();
            _ = Interlocked.Increment(ref _revision);
            this.UpdateNeighborConnectivity(worldTileX, worldTileY);
            _onStructureChanged?.Invoke(worldTileX, worldTileY, StructureTile.Empty);
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public bool ApplyDamage(int worldTileX, int worldTileY, float damageAmount, out StructureTile updatedStructure) =>
        this.ApplyDamage(worldTileX, worldTileY, damageAmount, byte.MaxValue, out updatedStructure);

    /// <inheritdoc/>
    public bool ApplyDamage(int worldTileX, int worldTileY, float damageAmount, byte toolHarvestTier, out StructureTile updatedStructure)
    {
        if (!_structures.TryGetValue((worldTileX, worldTileY), out StructureTile current))
        {
            updatedStructure = StructureTile.Empty;
            return false;
        }

        if (toolHarvestTier < current.Destructibility.HarvestTier)
        {
            updatedStructure = current;
            return false;
        }

        updatedStructure = current.ApplyDamage(damageAmount);
        _structures[(worldTileX, worldTileY)] = updatedStructure;

        if (updatedStructure.IsDestroyed)
        {
            // Structure collapsed; invalidate roof enclosures and disconnect adjacent walls
            _roofCache.Clear();
            _ = Interlocked.Increment(ref _revision);
            this.UpdateNeighborConnectivity(worldTileX, worldTileY);
        }

        _onStructureChanged?.Invoke(worldTileX, worldTileY, updatedStructure);
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
        _ = Interlocked.Increment(ref _revision);
        _onStructureChanged?.Invoke(worldTileX, worldTileY, updatedStructure);
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

    /// <inheritdoc/>
    public bool ResolveAabbCollision(ref Vector2 position, in AABB2D localBox, float tileSize, out Vector2 pushNormal)
    {
        pushNormal = Vector2.Zero;
        bool collided = false;

        AABB2D worldBox = localBox.Offset(position);
        int minTx = (int)MathF.Floor(worldBox.Min.X / tileSize);
        int maxTx = (int)MathF.Floor(worldBox.Max.X / tileSize);
        int minTy = (int)MathF.Floor(worldBox.Min.Y / tileSize);
        int maxTy = (int)MathF.Floor(worldBox.Max.Y / tileSize);

        for (int ty = minTy; ty <= maxTy; ty++)
        {
            for (int tx = minTx; tx <= maxTx; tx++)
            {
                if (!this.IsSolidAt(tx, ty))
                {
                    continue;
                }

                float boxMinX = tx * tileSize;
                float boxMinY = ty * tileSize;
                AABB2D tileBox = new(new Vector2(boxMinX, boxMinY), new Vector2(boxMinX + tileSize, boxMinY + tileSize));
                if (EntitySeparation.ResolveStaticAabb(ref position, in localBox, in tileBox, ref pushNormal))
                {
                    collided = true;
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
    public void Clear()
    {
        _structures.Clear();
        _roofCache.Clear();
        _ = Interlocked.Increment(ref _revision);
    }

    /// <summary>
    /// Replaces all constructed structures inside the specified chunk with the imported chunk delta state
    /// and stitches wall connectivity along the chunk boundary without firing mutation callbacks.
    /// </summary>
    internal void ApplyChunkStructures(int chunkX, int chunkY, IReadOnlyDictionary<int, StructureTile> structures)
    {
        _roofCache.Clear();
        _ = Interlocked.Increment(ref _revision);
        int baseX = chunkX * Chunk2D.Size;
        int baseY = chunkY * Chunk2D.Size;

        for (int ly = 0; ly < Chunk2D.Size; ly++)
        {
            int wy = baseY + ly;
            for (int lx = 0; lx < Chunk2D.Size; lx++)
            {
                int wx = baseX + lx;
                int localIndex = (ly * Chunk2D.Size) + lx;
                if (structures.TryGetValue(localIndex, out StructureTile incoming) && incoming.IsOccupied)
                {
                    _structures[(wx, wy)] = incoming;
                }
                else
                {
                    _ = _structures.TryRemove((wx, wy), out _);
                }
            }
        }

        for (int ly = -1; ly <= Chunk2D.Size; ly++)
        {
            int wy = baseY + ly;
            for (int lx = -1; lx <= Chunk2D.Size; lx++)
            {
                int wx = baseX + lx;
                if (_structures.TryGetValue((wx, wy), out StructureTile current) &&
                    !current.IsDestroyed &&
                    WallAutotiler.CanConnectToWall(current.Category))
                {
                    WallConnectivityMask mask = WallAutotiler.ComputeConnectivity(wx, wy, _isConnectableDelegate);
                    _structures[(wx, wy)] = current.WithConnectivity(mask);
                }
            }
        }
    }

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
                WallConnectivityMask updatedMask = WallAutotiler.ComputeConnectivity(nx, ny, _isConnectableDelegate);
                _structures[(nx, ny)] = neighbor.WithConnectivity(updatedMask);
                _onConnectivityUpdated?.Invoke(nx, ny, updatedMask);
            }
        }
    }

    /// <summary>
    /// Creates the default full-health tile of a structure type. Used when decoding wire deltas, which carry only the
    /// type and current durability; values must match <c>resources/config/structures.json</c>.
    /// </summary>
    /// <param name="type">The structure type.</param>
    /// <returns>The default tile, or <see cref="StructureTile.Empty"/> for unknown types.</returns>
    public static StructureTile CreateBlueprint(StructureType type) => type switch
    {
        StructureType.None => StructureTile.Empty,
        StructureType.WoodWall => StructureTile.CreateWall(type, StructureMaterial.Wood, durability: 150f, harvestTier: 1),
        StructureType.StoneWall => StructureTile.CreateWall(type, StructureMaterial.Stone, durability: 400f, harvestTier: 2),
        StructureType.WoodDoor => StructureTile.CreateDoor(type, StructureMaterial.Wood, durability: 100f, harvestTier: 1),
        StructureType.StoneDoor => StructureTile.CreateDoor(type, StructureMaterial.Stone, durability: 250f, harvestTier: 2),
        StructureType.WoodFloor => StructureTile.CreateFloor(type, StructureMaterial.Wood, moveSpeedMultiplier: 1.15f),
        StructureType.StoneFloor => StructureTile.CreateFloor(type, StructureMaterial.Stone, moveSpeedMultiplier: 1.20f),
        StructureType.RoofPillar => StructureTile.CreatePillar(StructureMaterial.Stone, durability: 300f),
        StructureType.ForgeAnvil => StructureTile.CreateStation(type, StructureMaterial.Metal, durability: 350f, harvestTier: 2),
        StructureType.MetalWall => StructureTile.CreateWall(type, StructureMaterial.Metal, durability: 550f, harvestTier: 3),
        StructureType.MetalDoor => StructureTile.CreateDoor(type, StructureMaterial.Metal, durability: 350f, harvestTier: 3),
        StructureType.Workbench => StructureTile.CreateStation(type, StructureMaterial.Wood, durability: 120f, harvestTier: 1),
        StructureType.Stove => StructureTile.CreateStation(type, StructureMaterial.Stone, durability: 200f, harvestTier: 1),
        StructureType.Campfire => StructureTile.CreateStation(type, StructureMaterial.Wood, durability: 40f, harvestTier: 0),
        StructureType.WoodChest => StructureTile.CreateFurniture(type, StructureMaterial.Wood, durability: 80f, harvestTier: 1),
        StructureType.WoodBed => StructureTile.CreateFurniture(type, StructureMaterial.Wood, durability: 90f, harvestTier: 1),
        StructureType.SandstoneWall => StructureTile.CreateWall(type, StructureMaterial.Stone, durability: 320f, harvestTier: 2),
        StructureType.WoodTable => StructureTile.CreateFurniture(type, StructureMaterial.Wood, durability: 70f, harvestTier: 1),
        StructureType.WoodShelf => StructureTile.CreateFurniture(type, StructureMaterial.Wood, durability: 70f, harvestTier: 1),
        StructureType.MarketStall => StructureTile.CreateStation(type, StructureMaterial.Wood, durability: 140f, harvestTier: 1),
        StructureType.PlankDeck => StructureTile.CreateFloor(type, StructureMaterial.Wood, moveSpeedMultiplier: 1.10f),
        _ => StructureTile.Empty
    };
}
