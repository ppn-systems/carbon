// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Abstractions.World;
using Carbon.Application.World.Replication;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
using Carbon.Maps.Streaming;
using Carbon.Simulation.ECS;
using Carbon.Simulation.World;

namespace Carbon.Application.World.Construction;

/// <summary>What a player's build job is doing.</summary>
internal enum BuildJobKind : byte
{
    None = 0,
    Construct = 1,
    Deconstruct = 2,
}

/// <summary>One player's build job and the last progress pushed to clients for it.</summary>
internal sealed class BuildJob
{
    public BuildJobKind Kind { get; set; }

    public int TileX { get; set; }

    public int TileY { get; set; }

    public int LastPushedBucket { get; set; } = -1;

    public ConstructionStage LastPushedStage { get; set; }

    /// <summary>Gets whether this is a construction job on the site anchored at (<paramref name="anchorX"/>, <paramref name="anchorY"/>).</summary>
    public bool IsConstructing(int anchorX, int anchorY) =>
        this.Kind == BuildJobKind.Construct && this.TileX == anchorX && this.TileY == anchorY;
}

/// <summary>
/// Server-authoritative construction (audit SOLID-1, extracted from <see cref="AuthoritativeWorld"/>): every place /
/// cancel / deconstruct / damage request goes through <see cref="BuildingService"/> (see <c>docs/building.md</c>).
/// Builders keep one <see cref="BuildJob"/> that advances each fixed tick while they stay within reach. Changes are
/// replicated through the world's outbound buffer and mark the touched chunks dirty. Called only under the world lock.
/// </summary>
internal sealed class ConstructionService
{
    private readonly BuildingService _building;
    private readonly ChunkWorld2D _chunkWorld;
    private readonly WorldSimulation _simulation;
    private readonly float _tileSize;
    private readonly Func<ItemRules> _itemRules;
    private readonly OutboundTickBuffer _outbound;
    private readonly HashSet<long> _dirtyChunks;
    private readonly Func<int, int, (int Offset, int Count)> _appendTileRecipients;
    private readonly GroundDropSink _groundDrops;

    /// <summary>Initializes a new instance of the <see cref="ConstructionService"/> class.</summary>
    public ConstructionService(
        ChunkWorld2D chunkWorld,
        StructureCatalog catalog,
        Func<int, int, bool> isPlayerOnTile,
        WorldSimulation simulation,
        float tileSize,
        Func<ItemRules> itemRules,
        OutboundTickBuffer outbound,
        HashSet<long> dirtyChunks,
        Func<int, int, (int Offset, int Count)> appendTileRecipients,
        Action<Vector2, ItemSlot> spawnGroundLoot)
    {
        _chunkWorld = chunkWorld ?? throw new ArgumentNullException(nameof(chunkWorld));
        _building = BuildingService.ForWorld(chunkWorld, catalog, new BuildPermissionTable(), isPlayerOnTile);
        _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
        _tileSize = tileSize;
        _itemRules = itemRules ?? throw new ArgumentNullException(nameof(itemRules));
        _outbound = outbound ?? throw new ArgumentNullException(nameof(outbound));
        _dirtyChunks = dirtyChunks ?? throw new ArgumentNullException(nameof(dirtyChunks));
        _appendTileRecipients = appendTileRecipients ?? throw new ArgumentNullException(nameof(appendTileRecipients));
        _groundDrops = new GroundDropSink(this, spawnGroundLoot ?? throw new ArgumentNullException(nameof(spawnGroundLoot)));
    }

    /// <summary>Gets the structure definitions used by the server.</summary>
    public StructureCatalog Catalog => _building.Catalog;

    /// <summary>Converts a build result into the command outcome reported to the client.</summary>
    public static WorldMutationOutcome ToOutcome(in BuildMutationResult result, uint revision) =>
        new(result.ToOpStatus(), result.IsSuccess ? revision : 0U);

    /// <summary>Resolves a material code from <c>structures.json</c> against the world's current <c>items.json</c> rules.</summary>
    public bool TryResolveItemId(string code, out uint itemId, out int maxStack)
    {
        if (_itemRules().ItemCatalog.TryGetByCode(code, out ItemCatalogEntry? entry))
        {
            itemId = entry.Id;
            maxStack = entry.MaxStack;
            return true;
        }

        itemId = 0;
        maxStack = 0;
        return false;
    }

    /// <summary>
    /// Builds the <see cref="BuildActor"/> for a player: its owner id (from <paramref name="characterId"/>), its tile and
    /// the best tool tier it carries. Fails when the player entity is gone.
    /// </summary>
    public bool TryCreateActor(Entity entity, Guid characterId, ReadOnlySpan<ItemSlot> inventory, out BuildActor actor)
    {
        if (!_simulation.Entities.IsAlive(entity) || !_simulation.Entities.HasTransform(entity))
        {
            actor = default;
            return false;
        }

        Vector2 pos = _simulation.Entities.GetTransform(entity).Position;
        ItemRules rules = _itemRules();
        byte toolTier = 0;
        foreach (ItemSlot slot in inventory)
        {
            if (!slot.IsEmpty &&
                rules.Items.TryGet(slot.ItemId, out ItemArchetype? def) &&
                def.Category == ItemCategory.Tool &&
                def.HarvestTier > toolTier)
            {
                toolTier = def.HarvestTier;
            }
        }

        actor = new BuildActor(
            OwnerIdOf(characterId),
            (int)MathF.Floor(pos.X / _tileSize),
            (int)MathF.Floor(pos.Y / _tileSize),
            toolTier);
        return true;
    }

    /// <summary>Places a blueprint, makes it the builder's job and delivers what the builder carries.</summary>
    public BuildMutationResult PlaceBlueprint(
        in BuildActor actor,
        BuildJob job,
        IMaterialSource materials,
        int tileX,
        int tileY,
        StructureType type,
        StructureRotation rotation)
    {
        BuildMutationResult placed = _building.PlaceBlueprint(actor, tileX, tileY, type, rotation);
        if (!placed.IsSuccess)
        {
            return placed;
        }

        this.StartJob(job, BuildJobKind.Construct, placed.AnchorX, placed.AnchorY);
        BuildMutationResult delivered = _building.DeliverMaterials(actor, materials, placed.AnchorX, placed.AnchorY);
        this.MarkFootprintDirty(placed);
        this.PushSiteProgress(job, placed.AnchorX, placed.AnchorY, force: true);
        return delivered.IsSuccess ? delivered : placed;
    }

    /// <summary>
    /// Cancels the blueprint under the tile (refunding delivered materials), or starts deconstructing the structure there
    /// (the first tick of work validates ownership and reach; the job then continues every tick).
    /// </summary>
    public BuildMutationResult Demolish(in BuildActor actor, BuildJob job, IMaterialSource materials, int tileX, int tileY, float firstTickWork)
    {
        BuildMutationResult result;
        if (_building.Ledger.TryGetSiteAt(tileX, tileY, out int anchorX, out int anchorY, out ConstructionSite site))
        {
            StructureType siteType = site.Type;
            result = _building.CancelBlueprint(actor, tileX, tileY, materials);
            if (result.IsSuccess)
            {
                this.QueueConstructionChange(anchorX, anchorY, siteType, StructureRotation.North, ConstructionStage.Removed, result.Width, result.Height, 0f, isDeconstruction: false);
                if (job.IsConstructing(anchorX, anchorY))
                {
                    job.Kind = BuildJobKind.None;
                }
            }
        }
        else
        {
            StructureType type = _chunkWorld.Buildings.TryGetStructure(tileX, tileY, out StructureTile tile) ? tile.Type : StructureType.None;
            result = _building.ApplyDeconstructionWork(actor, tileX, tileY, firstTickWork, materials);
            if (result.IsSuccess && result.Stage == ConstructionStage.Removed)
            {
                this.QueueFootprintStructureChanges(result);
            }
            else if (result.IsSuccess)
            {
                this.StartJob(job, BuildJobKind.Deconstruct, result.AnchorX, result.AnchorY);
                this.PushDeconstructionProgress(job, in result, type);
            }
        }

        if (result.IsSuccess)
        {
            this.MarkFootprintDirty(result);
        }

        return result;
    }

    /// <summary>Gets the builder work done in <paramref name="seconds"/>.</summary>
    public float WorkFor(float seconds) => _building.Catalog.Rules.BuilderWorkPerSecond * seconds;

    /// <summary>Gets the builder work done in one tick at <paramref name="tickRate"/> ticks per second.</summary>
    public float WorkPerTick(int tickRate) => _building.Catalog.Rules.BuilderWorkPerSecond / tickRate;

    /// <summary>Advances one player's construction or deconstruction job by <paramref name="work"/>.</summary>
    public void StepJob(BuildJob job, in BuildActor actor, IMaterialSource materials, float work)
    {
        if (job.Kind == BuildJobKind.Construct)
        {
            this.AdvanceConstruction(job, actor, materials, job.TileX, job.TileY, work);
        }
        else if (job.Kind == BuildJobKind.Deconstruct)
        {
            this.AdvanceDeconstruction(job, actor, materials, job.TileX, job.TileY, work);
        }
    }

    /// <summary>
    /// Damages a structure by a player's tool strike and drops a fraction of its materials when it is destroyed.
    /// Village (faction-owned) structures are refused (<see cref="BuildResultCode.ProtectedSettlement"/>).
    /// </summary>
    public BuildResultCode DamageStructure(int tileX, int tileY, float damage)
    {
        _groundDrops.Position = new Vector2((tileX + 0.5f) * _tileSize, (tileY + 0.5f) * _tileSize);
        BuildMutationResult result = _building.ApplyPlayerDamage(attackerId: 0UL, tileX, tileY, damage, _groundDrops);
        if (result.IsSuccess)
        {
            this.MarkFootprintDirty(result);
            this.QueueFootprintStructureChanges(result);
            if (result.Stage == ConstructionStage.Removed)
            {
                // Clears a deconstruction bar that may be showing on the destroyed structure.
                this.QueueConstructionChange(result.AnchorX, result.AnchorY, StructureType.None, StructureRotation.North, ConstructionStage.Removed, result.Width, result.Height, 0f, isDeconstruction: true);
            }
        }

        return result.Code;
    }

    /// <summary>Gets whether a tile belongs to a generated village (players may not modify it).</summary>
    public bool IsFactionProtected(int tileX, int tileY) => _building.IsFactionProtected(tileX, tileY);

    /// <summary>Gets the delta revision of the chunk holding the tile (0 when unmodified).</summary>
    public uint ChunkRevision(int tileX, int tileY) =>
        _chunkWorld.TryGetDelta(Chunk2D.ToChunkCoord(tileX), Chunk2D.ToChunkCoord(tileY), out ChunkDelta? delta) && delta is not null
            ? delta.Revision
            : 0U;

    private static ulong OwnerIdOf(Guid characterId)
    {
        Span<byte> bytes = stackalloc byte[16];
        _ = characterId.TryWriteBytes(bytes);
        ulong id = BinaryPrimitives.ReadUInt64LittleEndian(bytes) ^ BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]);
        return StructureOwners.ToPlayerOwnerId(id);
    }

    /// <summary>
    /// A paused job resumes on its own: the builder walked away, still lacks materials, lacks a tool, or someone
    /// stands in the frame.
    /// </summary>
    private static bool IsPausedJob(BuildResultCode code) =>
        code is BuildResultCode.OutOfReach or BuildResultCode.MaterialsNotDelivered or
            BuildResultCode.ToolTierTooLow or BuildResultCode.BlockedByEntity;

    /// <summary>Replaces the build job. A deconstruction that is abandoned clears its progress bar on clients.</summary>
    private void StartJob(BuildJob job, BuildJobKind kind, int anchorX, int anchorY)
    {
        if (job.Kind == BuildJobKind.Deconstruct &&
            (kind != BuildJobKind.Deconstruct || job.TileX != anchorX || job.TileY != anchorY))
        {
            this.QueueConstructionChange(job.TileX, job.TileY, StructureType.None, StructureRotation.North, ConstructionStage.Removed, 1, 1, 0f, isDeconstruction: true);
        }

        job.Kind = kind;
        job.TileX = anchorX;
        job.TileY = anchorY;
        job.LastPushedBucket = -1;
        job.LastPushedStage = ConstructionStage.None;
    }

    private void AdvanceConstruction(BuildJob job, in BuildActor actor, IMaterialSource materials, int jx, int jy, float work)
    {
        if (_building.Ledger.TryGetSiteAt(jx, jy, out _, out _, out ConstructionSite site) && site.Stage == ConstructionStage.Blueprint)
        {
            BuildMutationResult delivered = _building.DeliverMaterials(actor, materials, jx, jy);
            if (delivered.IsSuccess)
            {
                this.MarkFootprintDirty(delivered);
            }
        }

        BuildMutationResult result = _building.ApplyConstructionWork(actor, jx, jy, work);
        if (result.IsSuccess && result.Stage == ConstructionStage.Complete)
        {
            this.MarkFootprintDirty(result);
            this.QueueFootprintStructureChanges(result);
            this.QueueConstructionChange(result.AnchorX, result.AnchorY, StructureType.None, StructureRotation.North, ConstructionStage.Removed, result.Width, result.Height, 1f, isDeconstruction: false);
            job.Kind = BuildJobKind.None;
            return;
        }

        if (result.IsSuccess)
        {
            this.MarkFootprintDirty(result);
        }
        else if (!IsPausedJob(result.Code))
        {
            job.Kind = BuildJobKind.None;
        }

        this.PushSiteProgress(job, jx, jy, force: false);
    }

    private void AdvanceDeconstruction(BuildJob job, in BuildActor actor, IMaterialSource materials, int jx, int jy, float work)
    {
        StructureType type = _chunkWorld.Buildings.TryGetStructure(jx, jy, out StructureTile tile) ? tile.Type : StructureType.None;
        BuildMutationResult result = _building.ApplyDeconstructionWork(actor, jx, jy, work, materials);
        if (result.IsSuccess && result.Stage == ConstructionStage.Removed)
        {
            this.MarkFootprintDirty(result);
            this.QueueFootprintStructureChanges(result);
            this.QueueConstructionChange(result.AnchorX, result.AnchorY, type, StructureRotation.North, ConstructionStage.Removed, result.Width, result.Height, 1f, isDeconstruction: true);
            job.Kind = BuildJobKind.None;
            return;
        }

        if (result.IsSuccess)
        {
            this.PushDeconstructionProgress(job, in result, type);
        }
        else if (!IsPausedJob(result.Code))
        {
            this.QueueConstructionChange(jx, jy, type, StructureRotation.North, ConstructionStage.Removed, 1, 1, 0f, isDeconstruction: true);
            job.Kind = BuildJobKind.None;
        }
    }

    /// <summary>Pushes the site's stage and progress when the stage changed or progress crossed a 10% step.</summary>
    private void PushSiteProgress(BuildJob job, int tileX, int tileY, bool force)
    {
        if (!_building.Ledger.TryGetSiteAt(tileX, tileY, out int ax, out int ay, out ConstructionSite site) ||
            !_building.Catalog.TryGet(site.Type, out StructureDefinition definition))
        {
            return;
        }

        float progress = definition.WorkAmount > 0f ? site.WorkDone / definition.WorkAmount : 0f;
        int bucket = (int)(progress * 10f);
        if (!force && bucket == job.LastPushedBucket && site.Stage == job.LastPushedStage)
        {
            return;
        }

        job.LastPushedBucket = bucket;
        job.LastPushedStage = site.Stage;
        this.QueueConstructionChange(ax, ay, site.Type, site.Rotation, site.Stage, site.Width, site.Height, progress, isDeconstruction: false);
    }

    private void PushDeconstructionProgress(BuildJob job, in BuildMutationResult result, StructureType type)
    {
        int bucket = (int)(result.Progress * 10f);
        if (bucket == job.LastPushedBucket && job.LastPushedStage == ConstructionStage.Complete)
        {
            return;
        }

        job.LastPushedBucket = bucket;
        job.LastPushedStage = ConstructionStage.Complete;
        StructureRotation rotation = _building.Ledger.TryGetRecord(result.AnchorX, result.AnchorY, out StructureRecord record)
            ? record.Rotation
            : StructureRotation.North;
        this.QueueConstructionChange(result.AnchorX, result.AnchorY, type, rotation, ConstructionStage.Complete, result.Width, result.Height, result.Progress, isDeconstruction: true);
    }

    private void QueueConstructionChange(
        int anchorX,
        int anchorY,
        StructureType type,
        StructureRotation rotation,
        ConstructionStage stage,
        int width,
        int height,
        float progress,
        bool isDeconstruction)
    {
        (int offset, int count) = _appendTileRecipients(anchorX, anchorY);
        if (count == 0)
        {
            return;
        }

        _outbound.Pending.ConstructionChanges.Add(new OutboundConstructionChange(
            offset,
            count,
            anchorX,
            anchorY,
            (byte)type,
            (byte)rotation,
            (byte)stage,
            (byte)(Math.Clamp(width, 1, 15) | (Math.Clamp(height, 1, 15) << 4)),
            Math.Clamp(progress, 0f, 1f),
            isDeconstruction));
    }

    private void MarkFootprintDirty(in BuildMutationResult result)
    {
        int width = Math.Max(1, result.Width);
        int height = Math.Max(1, result.Height);
        _ = _dirtyChunks.Add(Chunk2D.PackKey(Chunk2D.ToChunkCoord(result.AnchorX), Chunk2D.ToChunkCoord(result.AnchorY)));
        _ = _dirtyChunks.Add(Chunk2D.PackKey(
            Chunk2D.ToChunkCoord(result.AnchorX + width - 1),
            Chunk2D.ToChunkCoord(result.AnchorY + height - 1)));
    }

    private void QueueFootprintStructureChanges(in BuildMutationResult result)
    {
        for (int dy = 0; dy < result.Height; dy++)
        {
            for (int dx = 0; dx < result.Width; dx++)
            {
                int tx = result.AnchorX + dx;
                int ty = result.AnchorY + dy;
                (int offset, int count) = _appendTileRecipients(tx, ty);
                if (count == 0)
                {
                    continue;
                }

                StructureTile tile = _chunkWorld.Buildings.TryGetStructure(tx, ty, out StructureTile current) ? current : StructureTile.Empty;
                _outbound.Pending.StructureChanges.Add(new OutboundStructureChange(
                    offset,
                    count,
                    tx,
                    ty,
                    (byte)tile.Type,
                    tile.CurrentDurability,
                    (byte)tile.DoorState,
                    (byte)tile.Connectivity));
            }
        }
    }

    /// <summary><see cref="IMaterialSink"/> that drops materials as ground loot at <see cref="Position"/>.</summary>
    private sealed class GroundDropSink(ConstructionService owner, Action<Vector2, ItemSlot> spawnGroundLoot) : IMaterialSink
    {
        public Vector2 Position { get; set; }

        public void Add(string itemId, int quantity)
        {
            if (quantity > 0 && owner.TryResolveItemId(itemId, out uint id, out _))
            {
                spawnGroundLoot(this.Position, new ItemSlot(id, quantity));
            }
        }
    }
}
