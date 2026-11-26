// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using System.Threading.Tasks;
using Carbon.Abstractions.World;
using Carbon.Application.World.Commands;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;
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
        uint revision = 0;
        float durability;
        bool isDestroyed = false;

        lock (_gate)
        {
            if (!_playersByConnection.TryGetValue(connectionId, out ActivePlayerState? state))
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

            state.Cooldowns.StartCooldown(AttackAbilityId, _currentTick, this.ScaleAttackCooldownLocked(state, (uint)Math.Max(10, this.TickRate / 2)));

            ItemSlot activeSlot = state.Session.SelectedHotbarSlot < state.Inventory.Capacity
                ? state.Inventory.GetSlot(state.Session.SelectedHotbarSlot)
                : ItemSlot.Empty;
            ToolTargetKind targetKind = ToToolTargetKind(existing.Category);
            PropStrikeEvaluation eval = ToolUsagePolicy.EvaluatePropStrike(
                _itemRules,
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
                uint currentRev = _chunkWorld.TryGetDelta(cx, cy, out ChunkDelta? curDelta) && curDelta is not null
                    ? curDelta.Revision
                    : 0U;
                float existingDur = existing.Destructibility.CurrentDurability;
                this.CollectNearbyConnectionsLocked(tileX, tileY, _aoiRecipientsScratch);
                if (_aoiRecipientsScratch.Count > 0)
                {
                    recipientsCopy = [.. _aoiRecipientsScratch];
                }

                modifiedCopy = [.. _mutationSlotScratch];
                if (recipientsCopy is null && modifiedCopy.Length == 0)
                {
                    return new WorldMutationOutcome(OpStatus.Forbidden, 0);
                }

                outcomeStatus = OpStatus.Forbidden;
                durability = existingDur;
                revision = currentRev;
            }
            else
            {
                _ = _dirtyChunks.Add(Chunk2D.PackKey(cx, cy));
                if (_chunkWorld.TryGetDelta(cx, cy, out ChunkDelta? delta) && delta is not null)
                {
                    revision = delta.Revision;
                }

                durability = updated.Destructibility.CurrentDurability;
                isDestroyed = updated.Destructibility.IsDestroyed;

                if (isDestroyed)
                {
                    ItemSlot drop = PropHarvestCatalog.ResolveDrop(_itemRules, updated.Type, out uint propXp);
                    if (!drop.IsEmpty)
                    {
                        Vector2 propDropPos = new((tileX + 0.5f) * this.TileSize, (tileY + 0.5f) * this.TileSize);
                        this.SpawnGroundLootLocked(propDropPos, in drop);
                    }

                    this.GrantExperienceLocked(state, propXp > 0U ? propXp : 15U, out _);
                    this.MarkCharacterDirtyLocked(state, inventoryChanged: false);
                }

                this.CollectNearbyConnectionsLocked(tileX, tileY, _aoiRecipientsScratch);
                if (_aoiRecipientsScratch.Count > 0)
                {
                    recipientsCopy = [.. _aoiRecipientsScratch];
                }

                modifiedCopy = [.. _mutationSlotScratch];
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
            if (!_playersByConnection.TryGetValue(connectionId, out ActivePlayerState? state) ||
                !_simulation.Entities.HasTransform(state.Entity))
            {
                return new WorldMutationOutcome(OpStatus.Forbidden, 0);
            }

            if (!this.IsWithinInteractRangeLocked(state.Entity, tileX, tileY))
            {
                return new WorldMutationOutcome(OpStatus.OutOfRange, 0);
            }

            BuildMutationResult result = this.PlaceBlueprintLocked(state, tileX, tileY, structureType, rotation);
            return ToOutcome(result, this.ChunkRevisionLocked(tileX, tileY));
        }
    }

    /// <summary>Synchronous form used by the tick and tests; handlers use the matching <c>Submit*Async</c> (ADR-017).</summary>
    public WorldMutationOutcome TryDemolishStructure(ulong connectionId, int tileX, int tileY)
    {
        lock (_gate)
        {
            if (!_playersByConnection.TryGetValue(connectionId, out ActivePlayerState? state) ||
                !this.TryGetBuildActorLocked(state, out BuildActor actor))
            {
                return new WorldMutationOutcome(OpStatus.Forbidden, 0);
            }

            if (!this.IsWithinInteractRangeLocked(state.Entity, tileX, tileY))
            {
                return new WorldMutationOutcome(OpStatus.OutOfRange, 0);
            }

            _playerMaterials.Bind(state);
            BuildMutationResult result;
            if (_building.Ledger.TryGetSiteAt(tileX, tileY, out int anchorX, out int anchorY, out ConstructionSite site))
            {
                StructureType siteType = site.Type;
                result = _building.CancelBlueprint(actor, tileX, tileY, _playerMaterials);
                if (result.IsSuccess)
                {
                    this.QueueConstructionChangeLocked(anchorX, anchorY, siteType, StructureRotation.North, ConstructionStage.Removed, result.Width, result.Height, 0f, isDeconstruction: false);
                    if (state.BuildJob == BuildJobKind.Construct && state.BuildJobTileX == anchorX && state.BuildJobTileY == anchorY)
                    {
                        state.BuildJob = BuildJobKind.None;
                    }
                }
            }
            else
            {
                // The first tick of work validates ownership and reach; the job then continues every tick.
                StructureType type = _chunkWorld.Buildings.TryGetStructure(tileX, tileY, out StructureTile tile) ? tile.Type : StructureType.None;
                result = _building.ApplyDeconstructionWork(actor, tileX, tileY, _building.Catalog.Rules.BuilderWorkPerSecond / this.TickRate, _playerMaterials);
                if (result.IsSuccess && result.Stage == ConstructionStage.Removed)
                {
                    this.QueueFootprintStructureChangesLocked(result);
                }
                else if (result.IsSuccess)
                {
                    this.StartBuildJobLocked(state, BuildJobKind.Deconstruct, result.AnchorX, result.AnchorY);
                    this.PushDeconstructionProgressLocked(state, in result, type);
                }
            }

            if (result.IsSuccess)
            {
                this.MarkFootprintDirtyLocked(result);
            }

            return ToOutcome(result, this.ChunkRevisionLocked(tileX, tileY));
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
        ActivePlayerState state,
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

            return;
        }

        if (this.IsWithinInteractRangeLocked(state.Entity, aimTx, aimTy) &&
            _chunkWorld.Buildings.TryGetStructure(aimTx, aimTy, out StructureTile targetStruct) &&
            targetStruct.Type != StructureType.None)
        {
            float dmg = 20f;
            byte toolTier = 1;
            if (!activeSlot.IsEmpty && _itemRules.Items.TryGet(activeSlot.ItemId, out ItemArchetype? toolDef))
            {
                toolTier = Math.Max((byte)1, toolDef.HarvestTier);
                bool isWoodStruct = targetStruct.Material == StructureMaterial.Wood;
                if (isWoodStruct && toolDef.Capabilities.HasFlag(ToolCapabilities.ChopFlora))
                {
                    dmg = MathF.Max(40f, toolDef.FloraHarvestDamage);
                }
                else if (!isWoodStruct && toolDef.Capabilities.HasFlag(ToolCapabilities.MineMineral))
                {
                    dmg = MathF.Max(45f, toolDef.MineralHarvestDamage);
                }
                else
                {
                    dmg = MathF.Max(15f, toolDef.BaseDamage * 0.7f);
                }
            }

            if (toolTier >= targetStruct.Destructibility.HarvestTier)
            {
                this.DamageStructureLocked(aimTx, aimTy, dmg);
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
            _itemRules,
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
            ItemSlot drop = PropHarvestCatalog.ResolveDrop(_itemRules, updated.Type, out uint propXp);
            if (!drop.IsEmpty)
            {
                Vector2 propDropPos = new((wx + 0.5f) * this.TileSize, (wy + 0.5f) * this.TileSize);
                this.SpawnGroundLootLocked(propDropPos, in drop);
            }

            this.GrantExperienceLocked(state, propXp > 0U ? propXp : 15U, out _);
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

    private WorldMutationOutcome ExecuteCommandLocked(WorldCommand command) => command.Kind switch
    {
        WorldCommandKind.HarvestProp => this.TryHarvestProp(command.ConnectionId, command.TileX, command.TileY),
        WorldCommandKind.PlaceStructure => this.TryPlaceStructure(command.ConnectionId, command.TileX, command.TileY, command.StructureType, command.Rotation),
        WorldCommandKind.DemolishStructure => this.TryDemolishStructure(command.ConnectionId, command.TileX, command.TileY),
        WorldCommandKind.ToggleDoor => this.TryToggleDoor(command.ConnectionId, command.TileX, command.TileY),
        WorldCommandKind.SelectHotbarSlot => new(this.TrySelectHotbarSlot(command.ConnectionId, (byte)command.Arg0), 0),
        WorldCommandKind.CraftRecipe => new(this.TryCraftRecipe(command.ConnectionId, command.Arg0, command.Arg1), 0),
        WorldCommandKind.MoveInventorySlot => new(this.TryMoveInventorySlot(command.ConnectionId, command.Arg0, command.Arg1), 0),
        WorldCommandKind.UpgradeInventoryItem => new(this.TryUpgradeInventoryItem(command.ConnectionId, command.Arg0), 0),
        WorldCommandKind.DropInventoryItem => new(this.TryDropInventoryItem(command.ConnectionId, command.Arg0, command.Arg2), 0),
        WorldCommandKind.VendorBuy => new(this.TryVendorBuy(command.ConnectionId, command.Arg0, command.Arg1), 0),
        WorldCommandKind.VendorSell => new(this.TryVendorSell(command.ConnectionId, command.Arg0, command.Arg1), 0),
        WorldCommandKind.RepairItem => new(this.TryRepairItem(command.ConnectionId, command.Arg0), 0),
        WorldCommandKind.AllocateCharacterPoint => new(
            this.TryAllocateCharacterPoint(command.ConnectionId, (PointAllocationKind)command.Arg0, command.Arg1, (byte)command.Arg2),
            0),
        _ => new WorldMutationOutcome(OpStatus.Invalid, 0),
    };

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
}
