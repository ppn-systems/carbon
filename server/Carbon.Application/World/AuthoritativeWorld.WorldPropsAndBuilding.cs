// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Carbon.Abstractions.World;
using Carbon.Contracts.Common;
using Carbon.Contracts.Maps.S2C;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
using Carbon.Maps.Networking;
using Carbon.Maps.Props;
using Carbon.Maps.Streaming;
using Carbon.Simulation.ECS;

namespace Carbon.Application.World;

public sealed partial class AuthoritativeWorld
{
    /// <inheritdoc/>
    public WorldMutationOutcome TryHarvestProp(ulong connectionId, int tileX, int tileY)
    {
        bool broadcast;
        uint revision = 0;
        float durability;
        bool isDestroyed;

        lock (_gate)
        {
            if (!_playersByConnection.TryGetValue(connectionId, out ActivePlayerState? state))
            {
                return new WorldMutationOutcome(OpStatus.Forbidden, 0);
            }

            if (!this.IsWithinInteractRangeLocked(state.Entity, tileX, tileY))
            {
                return new WorldMutationOutcome(OpStatus.OutOfRange, 0);
            }

            int cx = Chunk2D.ToChunkCoord(tileX);
            int cy = Chunk2D.ToChunkCoord(tileY);
            ulong respawnAt = _currentTick + (ulong)(this.TickRate * 30);

            if (!_chunkWorld.TryGetPropAt(tileX, tileY, out MapProp existing) ||
                existing.Type == MapPropType.None ||
                existing.Destructibility.IsDestroyed)
            {
                return new WorldMutationOutcome(OpStatus.NotFound, 0);
            }

            if (!existing.Destructibility.IsDestructible)
            {
                return new WorldMutationOutcome(OpStatus.Forbidden, 0);
            }

            ItemSlot activeSlot = state.Session.SelectedHotbarSlot < state.Inventory.Capacity
                ? state.Inventory.GetSlot(state.Session.SelectedHotbarSlot)
                : ItemSlot.Empty;
            ToolTargetKind targetKind = ToToolTargetKind(existing.Category);
            PropStrikeEvaluation eval = ToolUsagePolicy.EvaluatePropStrike(
                in activeSlot,
                targetKind,
                existing.Destructibility.HarvestTier);

            _mutationSlotScratch.Clear();
            if (eval.DurabilityLoss > 0f &&
                this.DegradeActiveSlotDurabilityLocked(state, in activeSlot, eval.DurabilityLoss, out ItemSlot degradedSlot))
            {
                _mutationSlotScratch.Add((state.Session.SelectedHotbarSlot, degradedSlot));
            }

            if (!_chunkWorld.TryDamagePropAt(tileX, tileY, eval.Damage, eval.EffectiveTier, respawnAt, out MapProp updated))
            {
                this.CollectNearbyConnectionsLocked(tileX, tileY, _aoiRecipientsScratch);
                if (_aoiRecipientsScratch.Count > 0)
                {
                    uint currentRev = _chunkWorld.TryGetDelta(cx, cy, out ChunkDelta? curDelta) && curDelta is not null
                        ? curDelta.Revision
                        : 0U;
                    _outboundSink.SendPropChanged(
                        CollectionsMarshal.AsSpan(_aoiRecipientsScratch),
                        tileX,
                        tileY,
                        currentRev,
                        existing.Destructibility.CurrentDurability,
                        isDestroyed: false);
                }

                this.FlushMutationSlotsOutbound(connectionId);
                return new WorldMutationOutcome(OpStatus.Forbidden, 0);
            }

            _ = _dirtyChunks.Add(Chunk2D.PackKey(cx, cy));
            if (_chunkWorld.TryGetDelta(cx, cy, out ChunkDelta? delta) && delta is not null)
            {
                revision = delta.Revision;
            }

            durability = updated.Destructibility.CurrentDurability;
            isDestroyed = updated.Destructibility.IsDestroyed;

            if (isDestroyed)
            {
                ItemSlot drop = ResolvePropDrop(updated.Type);
                if (!drop.IsEmpty)
                {
                    Vector2 propDropPos = new((tileX + 0.5f) * this.TileSize, (tileY + 0.5f) * this.TileSize);
                    this.SpawnGroundLootLocked(propDropPos, in drop);
                }

                this.GrantExperienceLocked(state, 15U, out _);
                this.MarkCharacterDirtyLocked(state, inventoryChanged: false);
            }

            this.CollectNearbyConnectionsLocked(tileX, tileY, _aoiRecipientsScratch);
            broadcast = _aoiRecipientsScratch.Count > 0;
        }

        if (broadcast)
        {
            _outboundSink.SendPropChanged(CollectionsMarshal.AsSpan(_aoiRecipientsScratch), tileX, tileY, revision, durability, isDestroyed);
        }

        this.FlushMutationSlotsOutbound(connectionId);
        return new WorldMutationOutcome(OpStatus.Ok, revision);
    }

    /// <inheritdoc/>
    public WorldMutationOutcome TryPlaceStructure(ulong connectionId, int tileX, int tileY, StructureType structureType)
    {
        if (structureType == StructureType.None)
        {
            return new WorldMutationOutcome(OpStatus.Invalid, 0);
        }

        uint requiredKit = StarterGameCatalog.ResolveStructureKitItemId((byte)structureType);
        if (requiredKit == 0U)
        {
            return new WorldMutationOutcome(OpStatus.Invalid, 0);
        }

        bool broadcast;
        uint revision = 0;
        StructureTile placed;

        lock (_gate)
        {
            if (!_playersByConnection.TryGetValue(connectionId, out ActivePlayerState? state) ||
                !_simulation.Entities.HasTransform(state.Entity))
            {
                return new WorldMutationOutcome(OpStatus.Forbidden, 0);
            }

            if (!this.IsWithinInteractRangeLocked(state.Entity, tileX, tileY))
            {
                return new WorldMutationOutcome(OpStatus.OutOfRange, 0);
            }

            if (state.Inventory.CountItem(requiredKit) < 1)
            {
                return new WorldMutationOutcome(OpStatus.Conflict, 0);
            }

            Vector2 playerPos = _simulation.Entities.GetTransform(state.Entity).Position;
            int ptx = (int)MathF.Floor(playerPos.X / this.TileSize);
            int pty = (int)MathF.Floor(playerPos.Y / this.TileSize);
            if ((structureType is StructureType.WoodWall or StructureType.StoneWall or StructureType.ForgeAnvil) && tileX == ptx && tileY == pty)
            {
                return new WorldMutationOutcome(OpStatus.Conflict, 0);
            }

            MapTile tile = _chunkWorld.GetTileAtWorld(tileX, tileY);
            bool hasProp = _chunkWorld.HasSolidPropAt(tileX, tileY);
            PlacementValidationResult result = _chunkWorld.Buildings.TryPlaceStructure(
                tileX,
                tileY,
                structureType,
                tile.Biome,
                hasProp,
                out placed);

            if (result != PlacementValidationResult.Success)
            {
                OpStatus mapped = result is PlacementValidationResult.BlockedByExistingStructure or PlacementValidationResult.BlockedByNaturalObstacle
                    ? OpStatus.Conflict
                    : OpStatus.Invalid;
                return new WorldMutationOutcome(mapped, 0);
            }

            _mutationSlotScratch.Clear();
            RemoveItemTrackingChanges(state.Inventory, requiredKit, 1, _mutationSlotScratch);
            this.MarkCharacterDirtyLocked(state, inventoryChanged: true);

            int cx = Chunk2D.ToChunkCoord(tileX);
            int cy = Chunk2D.ToChunkCoord(tileY);
            _ = _dirtyChunks.Add(Chunk2D.PackKey(cx, cy));
            if (_chunkWorld.TryGetDelta(cx, cy, out ChunkDelta? delta) && delta is not null)
            {
                revision = delta.Revision;
            }

            this.CollectNearbyConnectionsLocked(tileX, tileY, _aoiRecipientsScratch);
            broadcast = _aoiRecipientsScratch.Count > 0;
        }

        if (broadcast)
        {
            _outboundSink.SendStructureChanged(
                CollectionsMarshal.AsSpan(_aoiRecipientsScratch),
                tileX,
                tileY,
                (byte)placed.Type,
                placed.CurrentDurability,
                (byte)placed.DoorState,
                (byte)placed.Connectivity);
        }

        this.FlushMutationSlotsOutbound(connectionId);
        return new WorldMutationOutcome(OpStatus.Ok, revision);
    }

    /// <inheritdoc/>
    public WorldMutationOutcome TryDemolishStructure(ulong connectionId, int tileX, int tileY)
    {
        bool broadcast;
        uint revision = 0;

        lock (_gate)
        {
            if (!_playersByConnection.TryGetValue(connectionId, out ActivePlayerState? state))
            {
                return new WorldMutationOutcome(OpStatus.Forbidden, 0);
            }

            if (!this.IsWithinInteractRangeLocked(state.Entity, tileX, tileY))
            {
                return new WorldMutationOutcome(OpStatus.OutOfRange, 0);
            }

            if (!_chunkWorld.Buildings.TryDemolishStructure(tileX, tileY))
            {
                return new WorldMutationOutcome(OpStatus.NotFound, 0);
            }

            int cx = Chunk2D.ToChunkCoord(tileX);
            int cy = Chunk2D.ToChunkCoord(tileY);
            _ = _dirtyChunks.Add(Chunk2D.PackKey(cx, cy));
            if (_chunkWorld.TryGetDelta(cx, cy, out ChunkDelta? delta) && delta is not null)
            {
                revision = delta.Revision;
            }

            this.CollectNearbyConnectionsLocked(tileX, tileY, _aoiRecipientsScratch);
            broadcast = _aoiRecipientsScratch.Count > 0;
        }

        if (broadcast)
        {
            _outboundSink.SendStructureChanged(CollectionsMarshal.AsSpan(_aoiRecipientsScratch), tileX, tileY, 0, 0f, 0, 0);
        }

        return new WorldMutationOutcome(OpStatus.Ok, revision);
    }

    /// <inheritdoc/>
    public WorldMutationOutcome TryToggleDoor(ulong connectionId, int tileX, int tileY)
    {
        bool broadcast;
        uint revision = 0;
        StructureTile updated;

        lock (_gate)
        {
            if (!_playersByConnection.TryGetValue(connectionId, out ActivePlayerState? state))
            {
                return new WorldMutationOutcome(OpStatus.Forbidden, 0);
            }

            if (!this.IsWithinInteractRangeLocked(state.Entity, tileX, tileY))
            {
                return new WorldMutationOutcome(OpStatus.OutOfRange, 0);
            }

            if (!_chunkWorld.Buildings.ToggleDoor(tileX, tileY, out updated))
            {
                return new WorldMutationOutcome(OpStatus.NotFound, 0);
            }

            int cx = Chunk2D.ToChunkCoord(tileX);
            int cy = Chunk2D.ToChunkCoord(tileY);
            _ = _dirtyChunks.Add(Chunk2D.PackKey(cx, cy));
            if (_chunkWorld.TryGetDelta(cx, cy, out ChunkDelta? delta) && delta is not null)
            {
                revision = delta.Revision;
            }

            this.CollectNearbyConnectionsLocked(tileX, tileY, _aoiRecipientsScratch);
            broadcast = _aoiRecipientsScratch.Count > 0;
        }

        if (broadcast)
        {
            _outboundSink.SendStructureChanged(
                CollectionsMarshal.AsSpan(_aoiRecipientsScratch),
                tileX,
                tileY,
                (byte)updated.Type,
                updated.CurrentDurability,
                (byte)updated.DoorState,
                (byte)updated.Connectivity);
        }

        return new WorldMutationOutcome(OpStatus.Ok, revision);
    }

    /// <inheritdoc/>
    public bool TryCreateChunkDeltaPacket(int chunkX, int chunkY, uint knownRevision, out MapChunkDelta? packet)
    {
        lock (_gate)
        {
            if (_chunkWorld.TryGetDelta(chunkX, chunkY, out ChunkDelta? delta) &&
                delta is not null &&
                !delta.IsEmpty &&
                delta.Revision > knownRevision)
            {
                packet = ChunkDeltaPacketMapper.ToPacket(delta, isEndOfStream: true);
                return true;
            }

            packet = null;
            return false;
        }
    }

    private void ProcessPlaceablePrimaryActionLocked(
        ActivePlayerState state,
        Vector2 attackerPos,
        ItemSlot activeSlot,
        ItemArchetype archetype)
    {
        StructureType structureType = (StructureType)archetype.PlaceableStructureType;
        if (structureType == StructureType.None)
        {
            return;
        }

        int ptx = (int)MathF.Floor(attackerPos.X / this.TileSize);
        int pty = (int)MathF.Floor(attackerPos.Y / this.TileSize);
        int aimTx = (int)MathF.Floor(state.PendingAttackAim.X / this.TileSize);
        int aimTy = (int)MathF.Floor(state.PendingAttackAim.Y / this.TileSize);

        if (!this.IsWithinInteractRangeLocked(state.Entity, aimTx, aimTy))
        {
            return;
        }

        if ((structureType is StructureType.WoodWall or StructureType.StoneWall or StructureType.ForgeAnvil) && aimTx == ptx && aimTy == pty)
        {
            return;
        }

        MapTile tile = _chunkWorld.GetTileAtWorld(aimTx, aimTy);
        bool hasProp = _chunkWorld.HasSolidPropAt(aimTx, aimTy);
        if (_chunkWorld.Buildings.TryPlaceStructure(aimTx, aimTy, structureType, tile.Biome, hasProp, out StructureTile placed)
            != PlacementValidationResult.Success)
        {
            return;
        }

        ItemSlot updatedSlot = activeSlot.Quantity > 1
            ? activeSlot with { Quantity = activeSlot.Quantity - 1 }
            : ItemSlot.Empty;
        state.Inventory.SetSlot(state.Session.SelectedHotbarSlot, in updatedSlot);
        _pendingSlotChanges.Add(new OutboundSlotChange(state.Session.ConnectionId, state.Session.SelectedHotbarSlot, updatedSlot));
        this.MarkCharacterDirtyLocked(state, inventoryChanged: true);

        int cx = Chunk2D.ToChunkCoord(aimTx);
        int cy = Chunk2D.ToChunkCoord(aimTy);
        _ = _dirtyChunks.Add(Chunk2D.PackKey(cx, cy));

        (int offset, int count) = this.AppendTileRecipientsLocked(aimTx, aimTy);
        if (count > 0)
        {
            _pendingStructureChanges.Add(new OutboundStructureChange(
                offset,
                count,
                aimTx,
                aimTy,
                (byte)placed.Type,
                placed.CurrentDurability,
                (byte)placed.DoorState,
                (byte)placed.Connectivity));
        }
    }

    private void ProcessToolHarvestFallbackLocked(ActivePlayerState state, Vector2 attackerPos, Vector2 aimDir, in ItemSlot activeSlot)
    {
        int aimTx = (int)MathF.Floor(state.PendingAttackAim.X / this.TileSize);
        int aimTy = (int)MathF.Floor(state.PendingAttackAim.Y / this.TileSize);
        ulong respawnAt = _currentTick + (ulong)(this.TickRate * 30);

        int blockedTx = 0;
        int blockedTy = 0;
        MapProp blockedProp = default;
        bool hasBlocked = false;

        if (this.IsWithinHarvestRangeLocked(attackerPos, aimTx, aimTy))
        {
            if (this.TryDamagePropAndQueueLocked(state, aimTx, aimTy, in activeSlot, respawnAt, ref blockedProp))
            {
                return;
            }

            if (blockedProp.Type != MapPropType.None)
            {
                blockedTx = aimTx;
                blockedTy = aimTy;
                hasBlocked = true;
            }
        }

        if (!hasBlocked)
        {
            Vector2 sideDir = new(-aimDir.Y, aimDir.X);
            ReadOnlySpan<Vector2> probes =
            [
                attackerPos + (aimDir * 24f),
                attackerPos + (((aimDir * 0.85f) + (sideDir * 0.52f)) * 24f),
                attackerPos + (((aimDir * 0.85f) - (sideDir * 0.52f)) * 24f),
            ];

            for (int i = 0; i < probes.Length; i++)
            {
                int wx = (int)MathF.Floor(probes[i].X / this.TileSize);
                int wy = (int)MathF.Floor(probes[i].Y / this.TileSize);
                if (!this.IsWithinHarvestRangeLocked(attackerPos, wx, wy))
                {
                    continue;
                }

                MapProp localBlocked = default;
                if (this.TryDamagePropAndQueueLocked(state, wx, wy, in activeSlot, respawnAt, ref localBlocked))
                {
                    return;
                }

                if (localBlocked.Type != MapPropType.None)
                {
                    blockedTx = wx;
                    blockedTy = wy;
                    blockedProp = localBlocked;
                    hasBlocked = true;
                    break;
                }
            }
        }

        if (hasBlocked)
        {
            int cx = Chunk2D.ToChunkCoord(blockedTx);
            int cy = Chunk2D.ToChunkCoord(blockedTy);
            uint revision = _chunkWorld.TryGetDelta(cx, cy, out ChunkDelta? delta) && delta is not null
                ? delta.Revision
                : 0U;

            (int offset, int count) = this.AppendTileRecipientsLocked(blockedTx, blockedTy);
            if (count > 0)
            {
                _pendingPropChanges.Add(new OutboundPropChange(
                    offset,
                    count,
                    blockedTx,
                    blockedTy,
                    revision,
                    blockedProp.Destructibility.CurrentDurability,
                    false));
            }
        }
    }

    private bool TryDamagePropAndQueueLocked(
        ActivePlayerState state,
        int wx,
        int wy,
        in ItemSlot activeSlot,
        ulong respawnAt,
        ref MapProp tierBlockedProp)
    {
        if (!_simulation.Entities.IsAlive(state.Entity) ||
            !_simulation.Entities.HasTransform(state.Entity) ||
            !this.IsWithinHarvestRangeLocked(_simulation.Entities.GetTransform(state.Entity).Position, wx, wy))
        {
            return false;
        }

        if (!_chunkWorld.TryGetPropAt(wx, wy, out MapProp existing) ||
            existing.Type == MapPropType.None ||
            existing.Destructibility.IsDestroyed ||
            !existing.Destructibility.IsDestructible)
        {
            return false;
        }

        ToolTargetKind targetKind = ToToolTargetKind(existing.Category);
        PropStrikeEvaluation eval = ToolUsagePolicy.EvaluatePropStrike(
            in activeSlot,
            targetKind,
            existing.Destructibility.HarvestTier);

        if (eval.DurabilityLoss > 0f &&
            this.DegradeActiveSlotDurabilityLocked(state, in activeSlot, eval.DurabilityLoss, out ItemSlot degradedSlot))
        {
            _pendingSlotChanges.Add(new OutboundSlotChange(
                state.Session.ConnectionId,
                state.Session.SelectedHotbarSlot,
                degradedSlot));
        }

        if (!_chunkWorld.TryDamagePropAt(wx, wy, eval.Damage, eval.EffectiveTier, respawnAt, out MapProp updated))
        {
            if (eval.Outcome != ToolStrikeOutcome.Optimal || eval.EffectiveTier < existing.Destructibility.HarvestTier)
            {
                tierBlockedProp = existing;
            }

            return false;
        }

        int cx = Chunk2D.ToChunkCoord(wx);
        int cy = Chunk2D.ToChunkCoord(wy);
        _ = _dirtyChunks.Add(Chunk2D.PackKey(cx, cy));
        uint revision = _chunkWorld.TryGetDelta(cx, cy, out ChunkDelta? delta) && delta is not null
            ? delta.Revision
            : 0U;

        if (updated.Destructibility.IsDestroyed)
        {
            ItemSlot drop = ResolvePropDrop(updated.Type);
            if (!drop.IsEmpty)
            {
                Vector2 propDropPos = new((wx + 0.5f) * this.TileSize, (wy + 0.5f) * this.TileSize);
                this.SpawnGroundLootLocked(propDropPos, in drop);
            }

            this.GrantExperienceLocked(state, 15U, out _);
            this.MarkCharacterDirtyLocked(state, inventoryChanged: false);
        }

        (int offset, int count) = this.AppendTileRecipientsLocked(wx, wy);
        if (count > 0)
        {
            _pendingPropChanges.Add(new OutboundPropChange(
                offset,
                count,
                wx,
                wy,
                revision,
                updated.Destructibility.CurrentDurability,
                updated.Destructibility.IsDestroyed));
        }

        return true;
    }

    private (int Offset, int Count) AppendTileRecipientsLocked(int tileX, int tileY)
    {
        this.CollectNearbyConnectionsLocked(tileX, tileY, _aoiRecipientsScratch);
        int offset = _flatRecipients.Count;
        int count = _aoiRecipientsScratch.Count;
        for (int i = 0; i < count; i++)
        {
            _flatRecipients.Add(_aoiRecipientsScratch[i]);
        }

        return (offset, count);
    }

    private bool IsWithinHarvestRangeLocked(Vector2 playerPos, int tileX, int tileY)
    {
        Vector2 tileCenter = new((tileX + 0.5f) * this.TileSize, (tileY + 0.5f) * this.TileSize);
        return Vector2.DistanceSquared(playerPos, tileCenter) <= (ToolHarvestRangePx * ToolHarvestRangePx);
    }

    private bool IsWithinInteractRangeLocked(Entity entity, int tileX, int tileY)
    {
        if (!_simulation.Entities.IsAlive(entity) || !_simulation.Entities.HasTransform(entity))
        {
            return false;
        }

        Vector2 pos = _simulation.Entities.GetTransform(entity).Position;
        Vector2 tileCenter = new((tileX + 0.5f) * this.TileSize, (tileY + 0.5f) * this.TileSize);
        float maxDist = BuildInteractRangeTiles * this.TileSize;
        return Vector2.DistanceSquared(pos, tileCenter) <= (maxDist * maxDist);
    }

    private static ToolTargetKind ToToolTargetKind(PropCategory category) =>
        category == PropCategory.Mineral
            ? ToolTargetKind.Mineral
            : ToolTargetKind.Flora;

    private static ItemSlot ResolvePropDrop(MapPropType propType)
    {
        if (propType is MapPropType.OakTree or MapPropType.PineTree or MapPropType.SnowPine or
            MapPropType.PalmTree or MapPropType.BirchTree or MapPropType.WillowTree or
            MapPropType.DeadTree or MapPropType.AcaciaTree or MapPropType.CherryBlossomTree)
        {
            return new ItemSlot(StarterGameCatalog.WoodLog, 3);
        }

        if (propType is MapPropType.CopperVein or MapPropType.IronVein or MapPropType.GoldVein or MapPropType.SilverVein)
        {
            return new ItemSlot(StarterGameCatalog.IronOre, 2);
        }

        if (propType is MapPropType.CrystalCluster or MapPropType.GlacialCrystal)
        {
            return new ItemSlot(StarterGameCatalog.ManaCrystal, 2);
        }

        if (propType is MapPropType.Bush or MapPropType.Cactus or MapPropType.MushroomCluster)
        {
            return new ItemSlot(StarterGameCatalog.BerryHerb, 2);
        }

        if (propType is MapPropType.Boulder or MapPropType.BasaltPillar or MapPropType.CoalDeposit)
        {
            return new ItemSlot(StarterGameCatalog.StoneChunk, 3);
        }

        return ItemSlot.Empty;
    }
}
