// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Abstractions.World;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
using Carbon.Maps.Streaming;
using Carbon.Simulation.ECS;

namespace Carbon.Application.World;

/// <summary>
/// Server-authoritative construction: every place / cancel / deconstruct / damage request goes through
/// <see cref="BuildingService"/> (RimWorld-style lifecycle, see <c>docs/building.md</c>). Builders keep one job that
/// advances each fixed tick while they stay within reach.
/// </summary>
public sealed partial class AuthoritativeWorld
{
    private static readonly Dictionary<string, uint> s_itemIdsByCode = BuildItemCodeIndex();

    /// <summary>Gets the structure definitions used by the server.</summary>
    public StructureCatalog StructureCatalog => _building.Catalog;

    private static Dictionary<string, uint> BuildItemCodeIndex()
    {
        ReadOnlySpan<ItemArchetype> items = StarterGameCatalog.Items.All;
        Dictionary<string, uint> index = new(items.Length, StringComparer.Ordinal);
        foreach (ItemArchetype item in items)
        {
            _ = index.TryAdd(item.Code, item.Id);
        }

        return index;
    }

    /// <summary>
    /// Resolves a material code from <c>structures.json</c>: the loaded <c>items.json</c> catalog is authoritative,
    /// the built-in starter catalog is the fallback when the server runs without economy data (tests).
    /// </summary>
    private bool TryResolveItemId(string code, out uint itemId, out int maxStack)
    {
        if (_economy is not null && _economy.Items.TryGetByCode(code, out ItemCatalogEntry? entry))
        {
            itemId = entry.Id;
            maxStack = entry.MaxStack;
            return true;
        }

        if (s_itemIdsByCode.TryGetValue(code, out itemId))
        {
            maxStack = StarterGameCatalog.GetMaxStackSize(itemId);
            return true;
        }

        maxStack = 0;
        return false;
    }

    private static ulong OwnerIdOf(ActivePlayerState state)
    {
        Span<byte> bytes = stackalloc byte[16];
        _ = state.SourceCharacter.Id.TryWriteBytes(bytes);
        ulong id = BinaryPrimitives.ReadUInt64LittleEndian(bytes) ^ BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]);
        return id == 0UL ? 1UL : id;
    }

    private bool TryGetBuildActorLocked(ActivePlayerState state, out BuildActor actor)
    {
        if (!_simulation.Entities.IsAlive(state.Entity) || !_simulation.Entities.HasTransform(state.Entity))
        {
            actor = default;
            return false;
        }

        Vector2 pos = _simulation.Entities.GetTransform(state.Entity).Position;
        byte toolTier = 0;
        foreach (ItemSlot slot in state.Inventory.Slots)
        {
            if (!slot.IsEmpty &&
                StarterGameCatalog.Items.TryGet(slot.ItemId, out ItemArchetype? def) &&
                def.Category == ItemCategory.Tool &&
                def.HarvestTier > toolTier)
            {
                toolTier = def.HarvestTier;
            }
        }

        actor = new BuildActor(
            OwnerIdOf(state),
            (int)MathF.Floor(pos.X / this.TileSize),
            (int)MathF.Floor(pos.Y / this.TileSize),
            toolTier);
        return true;
    }

    private bool IsPlayerOnTileLocked(int tileX, int tileY)
    {
        foreach (KeyValuePair<ulong, ActivePlayerState> kvp in _playersByConnection)
        {
            Entity entity = kvp.Value.Entity;
            if (!_simulation.Entities.IsAlive(entity) || !_simulation.Entities.HasTransform(entity))
            {
                continue;
            }

            Vector2 pos = _simulation.Entities.GetTransform(entity).Position;
            if ((int)MathF.Floor(pos.X / this.TileSize) == tileX && (int)MathF.Floor(pos.Y / this.TileSize) == tileY)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Places a blueprint for <paramref name="state"/>, delivers what the player carries and makes it the player's build job.
    /// </summary>
    private BuildMutationResult PlaceBlueprintLocked(ActivePlayerState state, int tileX, int tileY, StructureType type, StructureRotation rotation)
    {
        if (!this.TryGetBuildActorLocked(state, out BuildActor actor))
        {
            return BuildMutationResult.Fail(BuildResultCode.PermissionDenied);
        }

        BuildMutationResult placed = _building.PlaceBlueprint(actor, tileX, tileY, type, rotation);
        if (!placed.IsSuccess)
        {
            return placed;
        }

        state.BuildJob = BuildJobKind.Construct;
        state.BuildJobTileX = placed.AnchorX;
        state.BuildJobTileY = placed.AnchorY;

        _playerMaterials.Bind(state);
        BuildMutationResult delivered = _building.DeliverMaterials(actor, _playerMaterials, placed.AnchorX, placed.AnchorY);
        this.MarkFootprintDirtyLocked(placed);
        return delivered.IsSuccess ? delivered : placed;
    }

    /// <summary>Advances every player's construction or deconstruction job by one fixed tick.</summary>
    private void ProcessBuildJobsLocked(float dt)
    {
        float work = _building.Catalog.Rules.BuilderWorkPerSecond * dt;
        foreach (KeyValuePair<ulong, ActivePlayerState> kvp in _playersByConnection)
        {
            ActivePlayerState state = kvp.Value;
            if (state.BuildJob == BuildJobKind.None || !this.TryGetBuildActorLocked(state, out BuildActor actor))
            {
                continue;
            }

            int jx = state.BuildJobTileX;
            int jy = state.BuildJobTileY;
            _playerMaterials.Bind(state);
            BuildMutationResult result;
            if (state.BuildJob == BuildJobKind.Construct)
            {
                if (_building.Ledger.TryGetSiteAt(jx, jy, out _, out _, out ConstructionSite site) && site.Stage == ConstructionStage.Blueprint)
                {
                    BuildMutationResult delivered = _building.DeliverMaterials(actor, _playerMaterials, jx, jy);
                    if (delivered.IsSuccess)
                    {
                        this.MarkFootprintDirtyLocked(delivered);
                    }
                }

                result = _building.ApplyConstructionWork(actor, jx, jy, work);
            }
            else
            {
                result = _building.ApplyDeconstructionWork(actor, jx, jy, work, _playerMaterials);
            }

            if (result.IsSuccess)
            {
                this.MarkFootprintDirtyLocked(result);
                if ((result.Stage == ConstructionStage.Complete && state.BuildJob == BuildJobKind.Construct) ||
                    result.Stage == ConstructionStage.Removed)
                {
                    this.QueueFootprintStructureChangesLocked(result);
                    state.BuildJob = BuildJobKind.None;
                }
            }
            else if (!IsPausedJob(result.Code))
            {
                state.BuildJob = BuildJobKind.None;
            }
        }
    }

    /// <summary>Damages a structure (tool strikes) and drops a fraction of its materials when it is destroyed.</summary>
    private void DamageStructureLocked(int tileX, int tileY, float damage)
    {
        _groundDrops.Position = new Vector2((tileX + 0.5f) * this.TileSize, (tileY + 0.5f) * this.TileSize);
        BuildMutationResult result = _building.ApplyDamage(tileX, tileY, damage, _groundDrops);
        if (result.IsSuccess)
        {
            this.MarkFootprintDirtyLocked(result);
            this.QueueFootprintStructureChangesLocked(result);
        }
    }

    private void MarkFootprintDirtyLocked(in BuildMutationResult result)
    {
        int width = Math.Max(1, result.Width);
        int height = Math.Max(1, result.Height);
        _ = _dirtyChunks.Add(Chunk2D.PackKey(Chunk2D.ToChunkCoord(result.AnchorX), Chunk2D.ToChunkCoord(result.AnchorY)));
        _ = _dirtyChunks.Add(Chunk2D.PackKey(
            Chunk2D.ToChunkCoord(result.AnchorX + width - 1),
            Chunk2D.ToChunkCoord(result.AnchorY + height - 1)));
    }

    private void QueueFootprintStructureChangesLocked(in BuildMutationResult result)
    {
        for (int dy = 0; dy < result.Height; dy++)
        {
            for (int dx = 0; dx < result.Width; dx++)
            {
                int tx = result.AnchorX + dx;
                int ty = result.AnchorY + dy;
                (int offset, int count) = this.AppendTileRecipientsLocked(tx, ty);
                if (count == 0)
                {
                    continue;
                }

                StructureTile tile = _chunkWorld.Buildings.TryGetStructure(tx, ty, out StructureTile current) ? current : StructureTile.Empty;
                _pendingStructureChanges.Add(new OutboundStructureChange(
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

    private uint ChunkRevisionLocked(int tileX, int tileY) =>
        _chunkWorld.TryGetDelta(Chunk2D.ToChunkCoord(tileX), Chunk2D.ToChunkCoord(tileY), out ChunkDelta? delta) && delta is not null
            ? delta.Revision
            : 0U;

    /// <summary>
    /// A paused job resumes on its own: the builder walked away, still lacks materials, lacks a tool, or someone
    /// stands in the frame.
    /// </summary>
    private static bool IsPausedJob(BuildResultCode code) =>
        code is BuildResultCode.OutOfReach or BuildResultCode.MaterialsNotDelivered or
            BuildResultCode.ToolTierTooLow or BuildResultCode.BlockedByEntity;

    private static WorldMutationOutcome ToOutcome(in BuildMutationResult result, uint revision) =>
        new(result.ToOpStatus(), result.IsSuccess ? revision : 0U);

    private enum BuildJobKind : byte
    {
        None = 0,
        Construct = 1,
        Deconstruct = 2
    }

    /// <summary>
    /// <see cref="IMaterialSource"/> over one player's inventory. Rebound per call so ticks do not allocate; slot changes
    /// are queued for the owning client and refunds that do not fit drop as ground loot.
    /// </summary>
    private sealed class PlayerMaterialAdapter(AuthoritativeWorld world) : IMaterialSource
    {
        private ActivePlayerState? _state;

        public void Bind(ActivePlayerState state) => _state = state;

        public int CountOf(string itemId) =>
            _state is not null && world.TryResolveItemId(itemId, out uint id, out _) ? _state.Inventory.CountItem(id) : 0;

        public int Take(string itemId, int quantity)
        {
            if (_state is null || !world.TryResolveItemId(itemId, out uint id, out _))
            {
                return 0;
            }

            int available = Math.Min(quantity, _state.Inventory.CountItem(id));
            if (available <= 0)
            {
                return 0;
            }

            InventoryContainer inventory = _state.Inventory;
            Span<ItemSlot> before = stackalloc ItemSlot[inventory.Slots.Length];
            inventory.Slots.CopyTo(before);
            int removed = inventory.TryRemove(id, available);
            this.QueueSlotChanges(before);
            return removed;
        }

        public void Add(string itemId, int quantity)
        {
            if (_state is null || quantity <= 0 || !world.TryResolveItemId(itemId, out uint id, out int maxStack))
            {
                return;
            }

            InventoryContainer inventory = _state.Inventory;
            Span<ItemSlot> before = stackalloc ItemSlot[inventory.Slots.Length];
            inventory.Slots.CopyTo(before);
            ItemSlot item = new(id, quantity);
            int remainder = PlayerInventoryLayout.TryAddBagItem(inventory, in item, maxStack);
            this.QueueSlotChanges(before);
            if (remainder > 0 && world._simulation.Entities.HasTransform(_state.Entity))
            {
                ItemSlot overflow = new(id, remainder);
                world.SpawnGroundLootLocked(world._simulation.Entities.GetTransform(_state.Entity).Position, in overflow);
            }
        }

        private void QueueSlotChanges(ReadOnlySpan<ItemSlot> before)
        {
            ActivePlayerState state = _state!;
            ReadOnlySpan<ItemSlot> after = state.Inventory.Slots;
            bool changed = false;
            for (int i = 0; i < after.Length; i++)
            {
                if (before[i] != after[i])
                {
                    world._pendingSlotChanges.Add(new OutboundSlotChange(state.Session.ConnectionId, (ushort)i, after[i]));
                    changed = true;
                }
            }

            if (changed)
            {
                world.MarkCharacterDirtyLocked(state, inventoryChanged: true);
            }
        }
    }

    /// <summary><see cref="IMaterialSink"/> that drops materials as ground loot at <see cref="Position"/>.</summary>
    private sealed class GroundDropSink(AuthoritativeWorld world) : IMaterialSink
    {
        public Vector2 Position { get; set; }

        public void Add(string itemId, int quantity)
        {
            if (quantity > 0 && world.TryResolveItemId(itemId, out uint id, out _))
            {
                ItemSlot drop = new(id, quantity);
                world.SpawnGroundLootLocked(this.Position, in drop);
            }
        }
    }
}
