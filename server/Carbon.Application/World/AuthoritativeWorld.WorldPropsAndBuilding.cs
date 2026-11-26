// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using System.Threading.Tasks;
using Carbon.Abstractions.World;
using Carbon.Application.World.Construction;
using Carbon.Application.World.Props;
using Carbon.Application.World.Players;
using Carbon.Application.World.Commands;
using Carbon.Contracts.Common;
using Carbon.Contracts.Maps.S2C;
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
    public ValueTask<WorldMutationOutcome> SubmitHarvestPropAsync(ulong connectionId, int tileX, int tileY) =>
        _commands.Submit(WorldCommandKind.HarvestProp, connectionId, tileX, tileY);

    /// <inheritdoc/>
    public ValueTask<WorldMutationOutcome> SubmitPlaceStructureAsync(ulong connectionId, int tileX, int tileY, StructureType structureType, StructureRotation rotation = StructureRotation.North) =>
        _commands.Submit(WorldCommandKind.PlaceStructure, connectionId, tileX, tileY, structureType, rotation);

    /// <inheritdoc/>
    public ValueTask<WorldMutationOutcome> SubmitDemolishStructureAsync(ulong connectionId, int tileX, int tileY) =>
        _commands.Submit(WorldCommandKind.DemolishStructure, connectionId, tileX, tileY);

    /// <inheritdoc/>
    public ValueTask<WorldMutationOutcome> SubmitToggleDoorAsync(ulong connectionId, int tileX, int tileY) =>
        _commands.Submit(WorldCommandKind.ToggleDoor, connectionId, tileX, tileY);

    /// <inheritdoc/>
    public void FailPendingCommands() => _commands.FailAll(OpStatus.Conflict);

    /// <summary>
    /// Harvests or damages the natural prop on the tile. Runs synchronously; packet handlers go through
    /// <see cref="SubmitHarvestPropAsync"/> so it executes inside the tick (ADR-017).
    /// </summary>
    public WorldMutationOutcome TryHarvestProp(ulong connectionId, int tileX, int tileY)
    {
        OpStatus outcomeStatus = OpStatus.Ok;
        ulong[]? recipientsCopy = null;
        (ushort SlotIndex, ItemSlot Slot)[] modifiedCopy;
        uint revision;
        float durability;
        bool isDestroyed = false;

        lock (_gate)
        {
            if (!_players.ByConnection.TryGetValue(connectionId, out PlayerState? state))
            {
                return new WorldMutationOutcome(OpStatus.Forbidden, 0);
            }

            // The "harvest nearby" key deliberately reaches the interact range (the client searches +/-2 tiles).
            if (!this.IsWithinInteractRangeLocked(state.Entity, tileX, tileY))
            {
                return new WorldMutationOutcome(OpStatus.OutOfRange, 0);
            }

            // Shares the attack cooldown with the tick-path tool swing, so the packet path cannot strike faster
            // than a normal swing (previously only the global per-connection packet limit applied).
            if (!state.Cooldowns.IsReady(AttackAbilityId, _currentTick))
            {
                return new WorldMutationOutcome(OpStatus.RateLimited, 0);
            }

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

            state.Cooldowns.StartCooldown(AttackAbilityId, _currentTick, _needs.ScaleAttackCooldown(state.Entity, (uint)Math.Max(10, this.TickRate / 2)));

            ItemSlot activeSlot = state.Session.SelectedHotbarSlot < state.Inventory.Capacity
                ? state.Inventory.GetSlot(state.Session.SelectedHotbarSlot)
                : ItemSlot.Empty;
            _mutationSlotScratch.Clear();
            PropStrikeResult strike = _props.Strike(state, tileX, tileY, in existing, in activeSlot, replicateToolWear: false, out ItemSlot? toolWear);
            if (toolWear.HasValue)
            {
                _mutationSlotScratch.Add((state.Session.SelectedHotbarSlot, toolWear.Value));
            }

            this.CollectNearbyConnectionsLocked(tileX, tileY, _aoiRecipientsScratch);
            if (_aoiRecipientsScratch.Count > 0)
            {
                recipientsCopy = [.. _aoiRecipientsScratch];
            }

            modifiedCopy = [.. _mutationSlotScratch];
            revision = strike.Revision;
            durability = strike.Prop.Destructibility.CurrentDurability;
            if (!strike.Damaged)
            {
                if (recipientsCopy is null && modifiedCopy.Length == 0)
                {
                    return new WorldMutationOutcome(OpStatus.Forbidden, 0);
                }

                outcomeStatus = OpStatus.Forbidden;
            }
            else
            {
                isDestroyed = strike.Prop.Destructibility.IsDestroyed;
            }
        }

        if (recipientsCopy is not null)
        {
            _outboundSink.SendPropChanged(recipientsCopy, tileX, tileY, revision, durability, isDestroyed);
        }

        this.SendSlotsOutbound(connectionId, modifiedCopy);
        return new WorldMutationOutcome(outcomeStatus, outcomeStatus == OpStatus.Ok ? revision : 0);
    }

    /// <summary>Synchronous form used by the tick and tests; handlers use the matching <c>Submit*Async</c> (ADR-017).</summary>
    public WorldMutationOutcome TryPlaceStructure(
        ulong connectionId,
        int tileX,
        int tileY,
        StructureType structureType,
        StructureRotation rotation = StructureRotation.North)
    {
        if (structureType == StructureType.None)
        {
            return new WorldMutationOutcome(OpStatus.Invalid, 0);
        }

        lock (_gate)
        {
            if (!_players.ByConnection.TryGetValue(connectionId, out PlayerState? state) ||
                !_simulation.Entities.HasTransform(state.Entity))
            {
                return new WorldMutationOutcome(OpStatus.Forbidden, 0);
            }

            if (!this.IsWithinInteractRangeLocked(state.Entity, tileX, tileY))
            {
                return new WorldMutationOutcome(OpStatus.OutOfRange, 0);
            }

            BuildMutationResult result = this.PlaceBlueprintLocked(state, tileX, tileY, structureType, rotation);
            return ConstructionService.ToOutcome(result, _construction.ChunkRevision(tileX, tileY));
        }
    }

    /// <summary>Synchronous form used by the tick and tests; handlers use the matching <c>Submit*Async</c> (ADR-017).</summary>
    public WorldMutationOutcome TryDemolishStructure(ulong connectionId, int tileX, int tileY)
    {
        lock (_gate)
        {
            if (!_players.ByConnection.TryGetValue(connectionId, out PlayerState? state) ||
                !this.TryGetBuildActorLocked(state, out BuildActor actor))
            {
                return new WorldMutationOutcome(OpStatus.Forbidden, 0);
            }

            if (!this.IsWithinInteractRangeLocked(state.Entity, tileX, tileY))
            {
                return new WorldMutationOutcome(OpStatus.OutOfRange, 0);
            }

            _playerMaterials.Bind(state);
            BuildMutationResult result = _construction.Demolish(actor, state.Build, _playerMaterials, tileX, tileY, _construction.WorkPerTick(this.TickRate));
            return ConstructionService.ToOutcome(result, _construction.ChunkRevision(tileX, tileY));
        }
    }

    /// <summary>Synchronous form used by the tick and tests; handlers use the matching <c>Submit*Async</c> (ADR-017).</summary>
    public WorldMutationOutcome TryToggleDoor(ulong connectionId, int tileX, int tileY)
    {
        ulong[]? recipientsCopy = null;
        uint revision = 0;
        StructureTile updated;

        lock (_gate)
        {
            if (!_players.ByConnection.TryGetValue(connectionId, out PlayerState? state))
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
            if (_aoiRecipientsScratch.Count > 0)
            {
                recipientsCopy = [.. _aoiRecipientsScratch];
            }
        }

        if (recipientsCopy is not null)
        {
            _outboundSink.SendStructureChanged(
                recipientsCopy,
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

    /// <summary>
    /// A placeable kit in the active hotbar slot selects a blueprint: the primary action places it on the aimed tile.
    /// The kit is not consumed; the blueprint's materials are (see <c>docs/building.md</c>).
    /// </summary>
    private void ProcessPlaceablePrimaryActionLocked(
        PlayerState state,
        Vector2 attackerPos,
        ItemSlot activeSlot,
        ItemArchetype archetype)
    {
        _ = attackerPos;
        _ = activeSlot;
        StructureType structureType = (StructureType)archetype.PlaceableStructureType;
        if (structureType == StructureType.None)
        {
            return;
        }

        int aimTx = (int)MathF.Floor(state.PendingAttackAim.X / this.TileSize);
        int aimTy = (int)MathF.Floor(state.PendingAttackAim.Y / this.TileSize);
        if (this.IsWithinInteractRangeLocked(state.Entity, aimTx, aimTy))
        {
            _ = this.PlaceBlueprintLocked(state, aimTx, aimTy, structureType, StructureRotation.North);
        }
    }

    private (int Offset, int Count) AppendTileRecipientsLocked(int tileX, int tileY)
    {
        this.CollectNearbyConnectionsLocked(tileX, tileY, _aoiRecipientsScratch);
        int offset = _outbound.Pending.Recipients.Count;
        int count = _aoiRecipientsScratch.Count;
        for (int i = 0; i < count; i++)
        {
            _outbound.Pending.Recipients.Add(_aoiRecipientsScratch[i]);
        }

        return (offset, count);
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
}
