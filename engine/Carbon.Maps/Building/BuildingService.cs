// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Maps.Data;
using Carbon.Maps.Streaming;

namespace Carbon.Maps.Building;

/// <summary>
/// Server-authoritative RimWorld-style construction lifecycle:
/// <c>PlaceBlueprint → DeliverMaterials → Frame → ApplyConstructionWork → Complete</c>, plus cancel (100% refund),
/// deconstruction (work + partial refund) and destruction by damage (smaller drop).
/// Every mutation validates ownership, reach, terrain and footprint rules and returns a <see cref="BuildMutationResult"/>.
/// State lives in <see cref="BuildingGrid"/> and <see cref="ConstructionLedger"/>, which a <see cref="ChunkWorld2D"/>
/// mirrors into chunk deltas for persistence and sync. Not thread-safe: call under the world lock / on the tick thread.
/// </summary>
public sealed class BuildingService
{
    private readonly BuildingGrid _grid;
    private readonly ConstructionLedger _ledger;
    private readonly StructureCatalog _catalog;
    private readonly IConstructionEnvironment _environment;
    private readonly IBuildPermissions _permissions;
    private readonly Dictionary<(int X, int Y), float> _deconstructProgress = new();

    /// <summary>Initializes a new instance of the <see cref="BuildingService"/> class.</summary>
    /// <param name="grid">Grid of completed structures.</param>
    /// <param name="ledger">Store of construction sites and ownership records.</param>
    /// <param name="catalog">Structure definitions and rules.</param>
    /// <param name="environment">Terrain, prop and entity queries.</param>
    /// <param name="permissions">Ownership policy.</param>
    public BuildingService(
        BuildingGrid grid,
        ConstructionLedger ledger,
        StructureCatalog catalog,
        IConstructionEnvironment environment,
        IBuildPermissions permissions)
    {
        _grid = grid ?? throw new ArgumentNullException(nameof(grid));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
    }

    /// <summary>Gets the structure catalog.</summary>
    public StructureCatalog Catalog => _catalog;

    /// <summary>Gets the construction site / ownership store.</summary>
    public ConstructionLedger Ledger => _ledger;

    /// <summary>
    /// Creates a service over a streamed world, persisting every change into its chunk deltas.
    /// </summary>
    /// <param name="world">The chunk world.</param>
    /// <param name="catalog">Structure definitions and rules.</param>
    /// <param name="permissions">Ownership policy.</param>
    /// <param name="isEntityAt">Optional predicate reporting live entities on a tile.</param>
    /// <returns>The service.</returns>
    public static BuildingService ForWorld(
        ChunkWorld2D world,
        StructureCatalog catalog,
        IBuildPermissions permissions,
        Func<int, int, bool>? isEntityAt = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        return new BuildingService(world.Buildings, world.Construction, catalog, new ChunkWorldConstructionEnvironment(world, isEntityAt), permissions);
    }

    /// <summary>
    /// Validates a placement without mutating anything (used for client-side previews and by <see cref="PlaceBlueprint"/>).
    /// </summary>
    /// <param name="worldTileX">Anchor (top-left) world tile X.</param>
    /// <param name="worldTileY">Anchor (top-left) world tile Y.</param>
    /// <param name="type">Structure type.</param>
    /// <param name="rotation">Placement rotation.</param>
    /// <returns><see cref="BuildResultCode.Ok"/> or the first violated rule.</returns>
    public BuildResultCode ValidatePlacement(int worldTileX, int worldTileY, StructureType type, StructureRotation rotation)
    {
        if (!_catalog.TryGet(type, out StructureDefinition definition))
        {
            return BuildResultCode.UnknownStructure;
        }

        if (rotation != StructureRotation.North && !definition.Rotatable)
        {
            return BuildResultCode.RotationNotAllowed;
        }

        (int width, int height) = definition.GetFootprint(rotation);
        byte anchorHeight = _environment.GetTerrain(worldTileX, worldTileY).HeightLevel;
        for (int dy = 0; dy < height; dy++)
        {
            for (int dx = 0; dx < width; dx++)
            {
                BuildResultCode code = this.ValidateTile(definition, worldTileX + dx, worldTileY + dy, anchorHeight);
                if (code != BuildResultCode.Ok)
                {
                    return code;
                }
            }
        }

        if (definition.RequiresWallsOnSides && !this.HasWallsOnOppositeSides(worldTileX, worldTileY, width, height))
        {
            return BuildResultCode.RequiresWallsOnSides;
        }

        return BuildResultCode.Ok;
    }

    /// <summary>
    /// Places a validated blueprint owned by the actor. No materials are consumed yet.
    /// </summary>
    /// <param name="actor">Server-resolved acting player.</param>
    /// <param name="worldTileX">Anchor (top-left) world tile X.</param>
    /// <param name="worldTileY">Anchor (top-left) world tile Y.</param>
    /// <param name="type">Structure type.</param>
    /// <param name="rotation">Placement rotation.</param>
    /// <returns>The result; <see cref="ConstructionStage.Blueprint"/> on success (or Frame for free structures).</returns>
    public BuildMutationResult PlaceBlueprint(in BuildActor actor, int worldTileX, int worldTileY, StructureType type, StructureRotation rotation)
    {
        BuildResultCode code = this.ValidatePlacement(worldTileX, worldTileY, type, rotation);
        if (code != BuildResultCode.Ok)
        {
            return BuildMutationResult.Fail(code);
        }

        StructureDefinition definition = this.Definition(type);
        (int width, int height) = definition.GetFootprint(rotation);
        if (!IsWithinReach(actor, worldTileX, worldTileY, width, height, _catalog.Rules.PlacementReachTiles))
        {
            return BuildMutationResult.Fail(BuildResultCode.OutOfReach);
        }

        ConstructionSite site = new(type, rotation, actor.ActorId, width, height, definition.Cost.Count);
        if (definition.Cost.Count == 0)
        {
            site.Stage = ConstructionStage.Frame;
        }

        _ledger.AddSite(worldTileX, worldTileY, site);
        return SiteResult(site, worldTileX, worldTileY, definition);
    }

    /// <summary>
    /// Moves still-missing materials from the actor's inventory into the blueprint. Partial deliveries are kept;
    /// the site becomes a <see cref="ConstructionStage.Frame"/> once every cost entry is satisfied.
    /// </summary>
    /// <param name="actor">Server-resolved acting player.</param>
    /// <param name="source">The actor's inventory.</param>
    /// <param name="worldTileX">Any footprint tile X.</param>
    /// <param name="worldTileY">Any footprint tile Y.</param>
    /// <returns>The result.</returns>
    public BuildMutationResult DeliverMaterials(in BuildActor actor, IMaterialSource source, int worldTileX, int worldTileY)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!this.TryResolveSite(actor, worldTileX, worldTileY, _catalog.Rules.BuilderReachTiles, out int ax, out int ay, out ConstructionSite site, out StructureDefinition definition, out BuildMutationResult failure))
        {
            return failure;
        }

        if (site.Stage != ConstructionStage.Blueprint)
        {
            return SiteResult(site, ax, ay, definition);
        }

        int moved = 0;
        bool complete = true;
        for (int i = 0; i < definition.Cost.Count; i++)
        {
            MaterialCost cost = definition.Cost[i];
            int missing = cost.Quantity - site.GetDelivered(i);
            if (missing <= 0)
            {
                continue;
            }

            int taken = Math.Clamp(source.Take(cost.ItemId, missing), 0, missing);
            site.SetDelivered(i, site.GetDelivered(i) + taken);
            moved += taken;
            complete &= taken == missing;
        }

        if (moved == 0)
        {
            return new BuildMutationResult(BuildResultCode.InsufficientMaterials, site.Stage, ax, ay, site.Width, site.Height, 0f);
        }

        if (complete)
        {
            site.Stage = ConstructionStage.Frame;
        }

        _ledger.NotifySiteChanged(ax, ay, site);
        return SiteResult(site, ax, ay, definition);
    }

    /// <summary>
    /// Adds construction work to a frame. Call once per tick per working builder; completes the structure when the
    /// definition's work amount is reached (waiting while an entity stands inside a solid footprint).
    /// </summary>
    /// <param name="actor">Server-resolved acting player.</param>
    /// <param name="worldTileX">Any footprint tile X.</param>
    /// <param name="worldTileY">Any footprint tile Y.</param>
    /// <param name="workPoints">Work added this tick (positive).</param>
    /// <returns>The result; <see cref="ConstructionStage.Complete"/> when the structure was finished.</returns>
    public BuildMutationResult ApplyConstructionWork(in BuildActor actor, int worldTileX, int worldTileY, float workPoints)
    {
        if (!(workPoints > 0f))
        {
            return BuildMutationResult.Fail(BuildResultCode.InvalidArgument);
        }

        if (!this.TryResolveSite(actor, worldTileX, worldTileY, _catalog.Rules.BuilderReachTiles, out int ax, out int ay, out ConstructionSite site, out StructureDefinition definition, out BuildMutationResult failure))
        {
            return failure;
        }

        if (site.Stage != ConstructionStage.Frame)
        {
            return new BuildMutationResult(BuildResultCode.MaterialsNotDelivered, site.Stage, ax, ay, site.Width, site.Height, 0f);
        }

        if (actor.ToolTier < definition.ToolTier)
        {
            return new BuildMutationResult(BuildResultCode.ToolTierTooLow, site.Stage, ax, ay, site.Width, site.Height, Progress(site, definition));
        }

        site.WorkDone = MathF.Min(definition.WorkAmount, site.WorkDone + workPoints);
        if (site.WorkDone < definition.WorkAmount)
        {
            _ledger.NotifySiteChanged(ax, ay, site);
            return SiteResult(site, ax, ay, definition);
        }

        if (definition.IsSolid && this.IsAnyEntityInside(ax, ay, site.Width, site.Height))
        {
            _ledger.NotifySiteChanged(ax, ay, site);
            return new BuildMutationResult(BuildResultCode.BlockedByEntity, site.Stage, ax, ay, site.Width, site.Height, 1f);
        }

        _ = _ledger.RemoveSite(ax, ay);
        StructureTile tile = definition.CreateTile();
        for (int dy = 0; dy < site.Height; dy++)
        {
            for (int dx = 0; dx < site.Width; dx++)
            {
                int tx = ax + dx;
                int ty = ay + dy;
                StructureType floorBeneath = _grid.TryGetStructure(tx, ty, out StructureTile existing) && existing.Category == StructureCategory.Floor
                    ? existing.Type
                    : StructureType.None;
                _grid.PlaceStructure(tx, ty, in tile);
                _ledger.SetRecord(tx, ty, new StructureRecord(site.OwnerId, (byte)dx, (byte)dy, site.Rotation, floorBeneath));
            }
        }

        return new BuildMutationResult(BuildResultCode.Ok, ConstructionStage.Complete, ax, ay, site.Width, site.Height, 1f);
    }

    /// <summary>
    /// Cancels a blueprint or frame and refunds 100% of the delivered materials.
    /// </summary>
    /// <param name="actor">Server-resolved acting player.</param>
    /// <param name="worldTileX">Any footprint tile X.</param>
    /// <param name="worldTileY">Any footprint tile Y.</param>
    /// <param name="refundSink">Receives the refunded materials.</param>
    /// <returns>The result; <see cref="ConstructionStage.Removed"/> on success.</returns>
    public BuildMutationResult CancelBlueprint(in BuildActor actor, int worldTileX, int worldTileY, IMaterialSink refundSink)
    {
        ArgumentNullException.ThrowIfNull(refundSink);
        if (!this.TryResolveSite(actor, worldTileX, worldTileY, _catalog.Rules.PlacementReachTiles, out int ax, out int ay, out ConstructionSite site, out StructureDefinition definition, out BuildMutationResult failure))
        {
            return failure;
        }

        for (int i = 0; i < definition.Cost.Count; i++)
        {
            int delivered = site.GetDelivered(i);
            if (delivered > 0)
            {
                refundSink.Add(definition.Cost[i].ItemId, delivered);
            }
        }

        _ = _ledger.RemoveSite(ax, ay);
        return new BuildMutationResult(BuildResultCode.Ok, ConstructionStage.Removed, ax, ay, site.Width, site.Height, 0f);
    }

    /// <summary>
    /// Adds deconstruction work to a completed structure. Once the required work
    /// (<see cref="BuildingRules.DeconstructWorkFraction"/> of the build work) is reached the whole footprint is
    /// removed, wall autotiling and room/roof detection are refreshed, a covered floor is restored and
    /// <see cref="BuildingRules.DeconstructRefundFraction"/> of each material is refunded.
    /// </summary>
    /// <param name="actor">Server-resolved acting player.</param>
    /// <param name="worldTileX">Any footprint tile X.</param>
    /// <param name="worldTileY">Any footprint tile Y.</param>
    /// <param name="workPoints">Work added this tick (positive).</param>
    /// <param name="refundSink">Receives the refunded materials.</param>
    /// <returns>The result; <see cref="ConstructionStage.Removed"/> once deconstructed.</returns>
    public BuildMutationResult ApplyDeconstructionWork(in BuildActor actor, int worldTileX, int worldTileY, float workPoints, IMaterialSink refundSink)
    {
        ArgumentNullException.ThrowIfNull(refundSink);
        if (!(workPoints > 0f))
        {
            return BuildMutationResult.Fail(BuildResultCode.InvalidArgument);
        }

        if (!this.TryResolveStructure(worldTileX, worldTileY, out ResolvedStructure resolved))
        {
            return BuildMutationResult.Fail(BuildResultCode.NotFound);
        }

        if (!_permissions.CanModify(actor.ActorId, resolved.Record.OwnerId))
        {
            return resolved.Result(BuildResultCode.PermissionDenied, ConstructionStage.Complete, 0f);
        }

        if (!IsWithinReach(actor, resolved.AnchorX, resolved.AnchorY, resolved.Width, resolved.Height, _catalog.Rules.BuilderReachTiles))
        {
            return resolved.Result(BuildResultCode.OutOfReach, ConstructionStage.Complete, 0f);
        }

        float required = resolved.Definition is null
            ? 0f
            : resolved.Definition.WorkAmount * _catalog.Rules.DeconstructWorkFraction;
        (int X, int Y) key = (resolved.AnchorX, resolved.AnchorY);
        float done = (_deconstructProgress.TryGetValue(key, out float previous) ? previous : 0f) + workPoints;
        if (done < required)
        {
            _deconstructProgress[key] = done;
            return resolved.Result(BuildResultCode.Ok, ConstructionStage.Complete, done / required);
        }

        this.RemoveStructure(resolved, refundSink, _catalog.Rules.DeconstructRefundFraction);
        return resolved.Result(BuildResultCode.Ok, ConstructionStage.Removed, 1f);
    }

    /// <summary>
    /// Damages every tile of a completed structure (monster attacks, explosions). When it is destroyed the footprint
    /// is removed, a covered floor is restored and <see cref="BuildingRules.DestroyedDropFraction"/> of each material
    /// is dropped into <paramref name="dropSink"/>.
    /// </summary>
    /// <param name="worldTileX">Any footprint tile X.</param>
    /// <param name="worldTileY">Any footprint tile Y.</param>
    /// <param name="damage">Damage amount (positive).</param>
    /// <param name="dropSink">Optional receiver of dropped materials (ground loot).</param>
    /// <returns>The result; <see cref="BuildMutationResult.Progress"/> is the remaining hit point fraction.</returns>
    public BuildMutationResult ApplyDamage(int worldTileX, int worldTileY, float damage, IMaterialSink? dropSink)
    {
        if (!(damage > 0f))
        {
            return BuildMutationResult.Fail(BuildResultCode.InvalidArgument);
        }

        if (!this.TryResolveStructure(worldTileX, worldTileY, out ResolvedStructure resolved))
        {
            return BuildMutationResult.Fail(BuildResultCode.NotFound);
        }

        StructureTile anchorTile = StructureTile.Empty;
        for (int dy = 0; dy < resolved.Height; dy++)
        {
            for (int dx = 0; dx < resolved.Width; dx++)
            {
                if (_grid.ApplyDamage(resolved.AnchorX + dx, resolved.AnchorY + dy, damage, out StructureTile updated) && dx == 0 && dy == 0)
                {
                    anchorTile = updated;
                }
            }
        }

        if (!anchorTile.IsDestroyed)
        {
            float remaining = anchorTile.MaxDurability > 0f ? anchorTile.CurrentDurability / anchorTile.MaxDurability : 0f;
            return resolved.Result(BuildResultCode.Ok, ConstructionStage.Complete, remaining);
        }

        this.RemoveStructure(resolved, dropSink, _catalog.Rules.DestroyedDropFraction);
        return resolved.Result(BuildResultCode.Ok, ConstructionStage.Removed, 0f);
    }

    private static BuildMutationResult SiteResult(ConstructionSite site, int anchorX, int anchorY, StructureDefinition definition) =>
        new(BuildResultCode.Ok, site.Stage, anchorX, anchorY, site.Width, site.Height, Progress(site, definition));

    private static float Progress(ConstructionSite site, StructureDefinition definition) =>
        definition.WorkAmount > 0f ? site.WorkDone / definition.WorkAmount : 0f;

    private static bool IsWithinReach(in BuildActor actor, int anchorX, int anchorY, int width, int height, int reach)
    {
        int dx = Math.Max(Math.Max(anchorX - actor.TileX, actor.TileX - (anchorX + width - 1)), 0);
        int dy = Math.Max(Math.Max(anchorY - actor.TileY, actor.TileY - (anchorY + height - 1)), 0);
        return Math.Max(dx, dy) <= reach;
    }

    private StructureDefinition Definition(StructureType type) =>
        _catalog.TryGet(type, out StructureDefinition definition)
            ? definition
            : throw new InvalidOperationException($"Structure type {type} has no definition.");

    private BuildResultCode ValidateTile(StructureDefinition definition, int x, int y, byte anchorHeight)
    {
        MapTile terrain = _environment.GetTerrain(x, y);
        TerrainAffordance affordance = BuildingFoundation.GetAffordance(terrain.Biome);
        if (affordance == TerrainAffordance.None)
        {
            return BuildResultCode.BlockedByWater;
        }

        if (terrain.IsSolid || terrain.HeightLevel != anchorHeight)
        {
            return BuildResultCode.BlockedByCliff;
        }

        if (affordance < definition.Affordance)
        {
            return BuildResultCode.InsufficientAffordance;
        }

        if (_ledger.IsReserved(x, y))
        {
            return BuildResultCode.BlockedByConstructionSite;
        }

        bool hasFloor = false;
        if (_grid.TryGetStructure(x, y, out StructureTile existing) && existing.IsOccupied)
        {
            hasFloor = existing.Category == StructureCategory.Floor && !existing.IsDestroyed;
            if (!hasFloor || definition.Category == StructureCategory.Floor)
            {
                return BuildResultCode.BlockedByExistingStructure;
            }
        }

        if (_environment.HasSolidProp(x, y))
        {
            return BuildResultCode.BlockedByNaturalObstacle;
        }

        if (definition.IsSolid && _environment.IsOccupiedByEntity(x, y))
        {
            return BuildResultCode.BlockedByEntity;
        }

        if (definition.RequiresFloor && !hasFloor)
        {
            return BuildResultCode.RequiresFloor;
        }

        return BuildResultCode.Ok;
    }

    private bool HasWallsOnOppositeSides(int anchorX, int anchorY, int width, int height)
    {
        bool northSouth = true;
        for (int dx = 0; dx < width && northSouth; dx++)
        {
            northSouth = this.IsWallLike(anchorX + dx, anchorY - 1) && this.IsWallLike(anchorX + dx, anchorY + height);
        }

        if (northSouth)
        {
            return true;
        }

        bool eastWest = true;
        for (int dy = 0; dy < height && eastWest; dy++)
        {
            eastWest = this.IsWallLike(anchorX - 1, anchorY + dy) && this.IsWallLike(anchorX + width, anchorY + dy);
        }

        return eastWest;
    }

    private bool IsWallLike(int x, int y)
    {
        if (_grid.TryGetStructure(x, y, out StructureTile structure) && !structure.IsDestroyed)
        {
            return WallAutotiler.CanConnectToWall(structure.Category);
        }

        return _ledger.TryGetSiteAt(x, y, out _, out _, out ConstructionSite site) &&
            _catalog.TryGet(site.Type, out StructureDefinition definition) &&
            WallAutotiler.CanConnectToWall(definition.Category);
    }

    private bool IsAnyEntityInside(int anchorX, int anchorY, int width, int height)
    {
        for (int dy = 0; dy < height; dy++)
        {
            for (int dx = 0; dx < width; dx++)
            {
                if (_environment.IsOccupiedByEntity(anchorX + dx, anchorY + dy))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool TryResolveSite(
        in BuildActor actor,
        int worldTileX,
        int worldTileY,
        int reach,
        out int anchorX,
        out int anchorY,
        out ConstructionSite site,
        out StructureDefinition definition,
        out BuildMutationResult failure)
    {
        definition = null!;
        if (!_ledger.TryGetSiteAt(worldTileX, worldTileY, out anchorX, out anchorY, out site))
        {
            failure = BuildMutationResult.Fail(BuildResultCode.NotFound);
            return false;
        }

        if (!_catalog.TryGet(site.Type, out definition))
        {
            failure = BuildMutationResult.Fail(BuildResultCode.UnknownStructure);
            return false;
        }

        if (!_permissions.CanModify(actor.ActorId, site.OwnerId))
        {
            failure = new BuildMutationResult(BuildResultCode.PermissionDenied, site.Stage, anchorX, anchorY, site.Width, site.Height, 0f);
            return false;
        }

        if (!IsWithinReach(actor, anchorX, anchorY, site.Width, site.Height, reach))
        {
            failure = new BuildMutationResult(BuildResultCode.OutOfReach, site.Stage, anchorX, anchorY, site.Width, site.Height, 0f);
            return false;
        }

        failure = default;
        return true;
    }

    private bool TryResolveStructure(int worldTileX, int worldTileY, out ResolvedStructure resolved)
    {
        resolved = default;
        if (!_grid.TryGetStructure(worldTileX, worldTileY, out StructureTile tile) || !tile.IsOccupied)
        {
            return false;
        }

        // Structures without a record (world-generated, legacy) are unowned single tiles.
        bool hasRecord = _ledger.TryGetRecord(worldTileX, worldTileY, out StructureRecord record);
        StructureDefinition? definition = _catalog.TryGet(tile.Type, out StructureDefinition def) ? def : null;
        (int width, int height) = definition is not null && hasRecord ? definition.GetFootprint(record.Rotation) : (1, 1);
        resolved = new ResolvedStructure(worldTileX - record.AnchorOffsetX, worldTileY - record.AnchorOffsetY, width, height, record, definition);
        return true;
    }

    private void RemoveStructure(in ResolvedStructure resolved, IMaterialSink? sink, float refundFraction)
    {
        _ = _deconstructProgress.Remove((resolved.AnchorX, resolved.AnchorY));
        for (int dy = 0; dy < resolved.Height; dy++)
        {
            for (int dx = 0; dx < resolved.Width; dx++)
            {
                int tx = resolved.AnchorX + dx;
                int ty = resolved.AnchorY + dy;
                StructureType floorBeneath = _ledger.TryGetRecord(tx, ty, out StructureRecord record) ? record.FloorBeneath : StructureType.None;
                _ = _grid.TryDemolishStructure(tx, ty);
                _ledger.RemoveRecord(tx, ty);
                if (floorBeneath != StructureType.None && _catalog.TryGet(floorBeneath, out StructureDefinition floor))
                {
                    StructureTile floorTile = floor.CreateTile();
                    _grid.PlaceStructure(tx, ty, in floorTile);
                    _ledger.SetRecord(tx, ty, new StructureRecord(record.OwnerId, 0, 0, StructureRotation.North, StructureType.None));
                }
            }
        }

        if (sink is null || resolved.Definition is null)
        {
            return;
        }

        foreach (MaterialCost cost in resolved.Definition.Cost)
        {
            int quantity = (int)MathF.Floor(cost.Quantity * refundFraction);
            if (quantity > 0)
            {
                sink.Add(cost.ItemId, quantity);
            }
        }
    }

    private readonly record struct ResolvedStructure(
        int AnchorX,
        int AnchorY,
        int Width,
        int Height,
        StructureRecord Record,
        StructureDefinition? Definition)
    {
        public BuildMutationResult Result(BuildResultCode code, ConstructionStage stage, float progress) =>
            new(code, stage, this.AnchorX, this.AnchorY, this.Width, this.Height, progress);
    }
}
