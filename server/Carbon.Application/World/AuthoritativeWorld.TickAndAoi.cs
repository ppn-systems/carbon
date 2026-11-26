// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.AI.Components;
using Carbon.AI.Systems;
using Carbon.AI.Threat;
using Carbon.Contracts.Entities;
using Carbon.Items.Catalog;
using Carbon.Maps.Generators;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Maps.Data;
using Carbon.Maps.Streaming;
using Carbon.Simulation.Clock;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.Events;
using Carbon.Simulation.Networking;
using Carbon.Simulation.World;

namespace Carbon.Application.World;

public sealed partial class AuthoritativeWorld
{
    /// <inheritdoc/>
    public void StepFixedTick()
    {
        lock (_gate)
        {
            _dirtyResetScratch.Clear();

            _currentTick++;
            float dt = 1f / this.TickRate;

            foreach (KeyValuePair<ulong, ActivePlayerState> kvp in _playersByConnection)
            {
                ActivePlayerState state = kvp.Value;
                bool isMoving;
                bool isSprinting = false;
                if (state.HasPendingInput)
                {
                    isMoving = state.PendingDirection.LengthSquared() > 1e-4f;
                    isSprinting = isMoving && state.PendingSprint && state.CurrentStamina > 2f;
                    if (isSprinting)
                    {
                        state.CurrentStamina = MathF.Max(0f, state.CurrentStamina - (22f * dt));
                        state.StatsDirty = true;
                    }

                    float speed = (isSprinting ? SprintSpeed : BaseWalkSpeed) * state.Stats.MoveSpeedMultiplier;
                    _simulation.EnqueueCommand(SimulationCommand.MoveInput(
                        state.Entity,
                        state.PendingDirection,
                        speed,
                        state.LastClientTick));
                    state.HasPendingInput = false;
                }

                if (!isSprinting && state.CurrentStamina < state.Stats.MaxStamina)
                {
                    state.CurrentStamina = MathF.Min(state.Stats.MaxStamina, state.CurrentStamina + (18f * dt));
                }

                if (state.CurrentMana < state.Stats.MaxMana)
                {
                    float mpRegen = 6f + (state.Progression.GetAttribute(PrimaryAttributeId.Wisdom) * 0.35f);
                    state.CurrentMana = MathF.Min(state.Stats.MaxMana, state.CurrentMana + (mpRegen * dt));
                }

                if (state.PendingPickupLootId != 0UL)
                {
                    _ = this.TryPickupSpecificLootLocked(state, state.PendingPickupLootId, 84f * 84f);
                    state.PendingPickupLootId = 0UL;
                }
            }

            _simulation.Tick(new FixedTickArgs(_currentTick, dt, _currentTick * dt));

            this.StepProjectilesLocked();

            foreach (KeyValuePair<ulong, ActivePlayerState> kvp in _playersByConnection)
            {
                ActivePlayerState state = kvp.Value;
                if (state.HasPendingAttack)
                {
                    this.ProcessPlayerAttackLocked(state);
                    state.HasPendingAttack = false;
                }
            }

            this.ProcessBuildJobsLocked(dt);
            this.ProcessAiAttacksLocked();
            this.ProcessMonsterRespawnsLocked();
            this.ProcessDespawnedEntitiesLocked();

            if ((_currentTick % this.TickRate) == 0UL)
            {
                _respawnedPropsScratch.Clear();
                if (_chunkWorld.ProcessPropRespawns(_currentTick, _respawnedPropsScratch) > 0)
                {
                    for (int i = 0; i < _respawnedPropsScratch.Count; i++)
                    {
                        (int rtx, int rty, uint rRev, float maxDur) = _respawnedPropsScratch[i];
                        _ = _dirtyChunks.Add(Chunk2D.PackKey(Chunk2D.ToChunkCoord(rtx), Chunk2D.ToChunkCoord(rty)));
                        (int offset, int count) = this.AppendTileRecipientsLocked(rtx, rty);
                        if (count > 0)
                        {
                            _pendingPropChanges.Add(new OutboundPropChange(
                                offset,
                                count,
                                rtx,
                                rty,
                                rRev,
                                maxDur,
                                IsDestroyed: false));
                        }
                    }
                }
            }

            foreach (KeyValuePair<ulong, ActivePlayerState> kvp in _playersByConnection)
            {
                ActivePlayerState observer = kvp.Value;
                if (!_simulation.Entities.IsAlive(observer.Entity) || !_simulation.Entities.HasTransform(observer.Entity))
                {
                    continue;
                }

                Vector2 obsPos = _simulation.Entities.GetTransform(observer.Entity).Position;
                if ((_currentTick % 10UL) == 0UL)
                {
                    _chunkWorld.UpdateAroundPosition(obsPos, chunkRadius: 2, evictionRadius: 8);
                    this.PopulateChunkActorsAroundLocked(obsPos, radius: 2);
                }

                _aoi.ComputeVisibility(
                    observer.Session.ConnectionId,
                    observer.Session.PlayerNetworkId,
                    obsPos,
                    _simulation,
                    _scratchEntered,
                    _scratchVisible,
                    _scratchExited);

                for (int i = 0; i < _scratchEntered.Count; i++)
                {
                    ulong targetNetId = _scratchEntered[i];
                    if (this.TryBuildSpawnSnapshotLocked(targetNetId, out EntitySpawnSnapshot snap))
                    {
                        _pendingSpawns.Add(new OutboundSpawn(observer.Session.ConnectionId, snap));
                    }
                }

                for (int i = 0; i < _scratchExited.Count; i++)
                {
                    _pendingDespawns.Add(new OutboundDespawn(observer.Session.ConnectionId, _scratchExited[i], _currentTick));
                }

                int deltaOffset = _flatDeltaEntries.Count;
                for (int i = 0; i < _scratchVisible.Count; i++)
                {
                    ulong visibleNetId = _scratchVisible[i];
                    if (_simulation.TryGetEntityByNetworkId(visibleNetId, out Entity visibleEntity))
                    {
                        EntityDirtyMask mask = _simulation.GetDirtyFlags(visibleEntity);
                        if (mask != EntityDirtyMask.None)
                        {
                            Vector2 pos = _simulation.Entities.GetTransform(visibleEntity).Position;
                            Vector2 vel = _simulation.Entities.HasVelocity(visibleEntity)
                                ? _simulation.Entities.GetVelocity(visibleEntity).Linear
                                : Vector2.Zero;
                            float hp = _simulation.Entities.HasHealth(visibleEntity)
                                ? _simulation.Entities.GetHealth(visibleEntity).Current
                                : 100f;
                            _flatDeltaEntries.Add(new EntityDeltaEntry(visibleNetId, (byte)mask, pos.X, pos.Y, vel.X, vel.Y, hp));
                        }
                    }
                }

                int deltaCount = _flatDeltaEntries.Count - deltaOffset;
                EntityDirtyMask selfDirty = _simulation.GetDirtyFlags(observer.Entity);
                if (selfDirty != EntityDirtyMask.None || deltaCount > 0)
                {
                    _pendingDeltas.Add(new OutboundDelta(
                        observer.Session.ConnectionId,
                        _currentTick,
                        observer.LastClientTick,
                        obsPos.X,
                        obsPos.Y,
                        deltaOffset,
                        deltaCount));
                }

                if ((selfDirty & EntityDirtyMask.Transform) != 0)
                {
                    observer.PersistenceDirty = true;
                }

                // Out-of-combat natural HP regeneration (+3 HP/s after 6s without taking combat hits).
                if ((_currentTick % this.TickRate) == 0UL &&
                    (_currentTick - observer.LastCombatDamageTick) >= (ulong)(this.TickRate * 6) &&
                    _simulation.Entities.HasHealth(observer.Entity))
                {
                    HealthComponent php = _simulation.Entities.GetHealth(observer.Entity);
                    if (php.Current > 0f && php.Current < php.Max)
                    {
                        float healed = MathF.Min(php.Max, php.Current + 3f);
                        _simulation.Entities.SetHealth(observer.Entity, new HealthComponent(healed, php.Max));
                        _simulation.MarkDirty(observer.Entity, EntityDirtyMask.Health);
                        observer.PersistenceDirty = true;
                    }
                }

                // Friendly NPCs near settlements slowly mend damaged tools, blades, and armor within 110px.
                if ((_currentTick % (ulong)(this.TickRate * 2)) == 0UL &&
                    this.HasNearbyFriendlyNpcLocked(obsPos, 110f * 110f))
                {
                    for (int sIdx = 0; sIdx < observer.Inventory.Capacity; sIdx++)
                    {
                        ItemSlot curSlot = observer.Inventory.GetSlot(sIdx);
                        if (curSlot.IsEmpty ||
                            !StarterGameCatalog.Items.TryGet(curSlot.ItemId, out ItemArchetype? itemDef))
                        {
                            continue;
                        }

                        float effectiveMaxDur = ItemEnhancementPolicy.GetEffectiveMaxDurability(itemDef, curSlot.UpgradeLevel);
                        if (effectiveMaxDur <= 0f || curSlot.Durability >= effectiveMaxDur)
                        {
                            continue;
                        }

                        float mendedDur = MathF.Min(effectiveMaxDur, curSlot.Durability + 12f);
                        ItemSlot repaired = curSlot with { Durability = mendedDur };
                        observer.Inventory.SetSlot(sIdx, in repaired);
                        _pendingSlotChanges.Add(new OutboundSlotChange(observer.Session.ConnectionId, (ushort)sIdx, repaired));
                        this.MarkCharacterDirtyLocked(observer, inventoryChanged: true);
                    }
                }

                // Auto-collect nearby stackable/bag ground loot within 32px after brief drop grace period.
                this.CollectNearbyGroundLootLocked(observer, obsPos);

                if (observer.StatsDirty || (_currentTick % 10UL) == 0UL)
                {
                    this.EnqueueStatsSyncLocked(observer, observer.LeveledUpPending);
                    observer.StatsDirty = false;
                }
            }

            ReadOnlySpan<Entity> activeEntities = CollectionsMarshal.AsSpan((List<Entity>)_simulation.Entities.ActiveEntities);
            for (int i = 0; i < activeEntities.Length; i++)
            {
                _dirtyResetScratch.Add(activeEntities[i]);
            }

            for (int i = 0; i < _dirtyResetScratch.Count; i++)
            {
                _simulation.ClearDirtyFlags(_dirtyResetScratch[i]);
            }

            this.SwapOutboundBuffersLocked();
        }

        this.DispatchPendingTickOutputs();
    }

    private void SwapOutboundBuffersLocked()
    {
        (_pendingSpawns, _dispatchSpawns) = (_dispatchSpawns, _pendingSpawns);
        (_pendingDespawns, _dispatchDespawns) = (_dispatchDespawns, _pendingDespawns);
        (_pendingDeltas, _dispatchDeltas) = (_dispatchDeltas, _pendingDeltas);
        (_flatDeltaEntries, _dispatchDeltaEntries) = (_dispatchDeltaEntries, _flatDeltaEntries);
        (_pendingCombatHits, _dispatchCombatHits) = (_dispatchCombatHits, _pendingCombatHits);
        (_pendingSlotChanges, _dispatchSlotChanges) = (_dispatchSlotChanges, _pendingSlotChanges);
        (_pendingPropChanges, _dispatchPropChanges) = (_dispatchPropChanges, _pendingPropChanges);
        (_pendingStructureChanges, _dispatchStructureChanges) = (_dispatchStructureChanges, _pendingStructureChanges);
        (_pendingConstructionChanges, _dispatchConstructionChanges) = (_dispatchConstructionChanges, _pendingConstructionChanges);
        (_pendingEquipmentChanges, _dispatchEquipmentChanges) = (_dispatchEquipmentChanges, _pendingEquipmentChanges);
        (_pendingStatsSyncs, _dispatchStatsSyncs) = (_dispatchStatsSyncs, _pendingStatsSyncs);
        (_pendingSkillCasts, _dispatchSkillCasts) = (_dispatchSkillCasts, _pendingSkillCasts);
        (_flatRecipients, _dispatchRecipients) = (_dispatchRecipients, _flatRecipients);

        _pendingSpawns.Clear();
        _pendingDespawns.Clear();
        _pendingDeltas.Clear();
        _flatDeltaEntries.Clear();
        _pendingCombatHits.Clear();
        _pendingSlotChanges.Clear();
        _pendingPropChanges.Clear();
        _pendingStructureChanges.Clear();
        _pendingConstructionChanges.Clear();
        _pendingEquipmentChanges.Clear();
        _pendingStatsSyncs.Clear();
        _pendingSkillCasts.Clear();
        _flatRecipients.Clear();
    }

    /// <inheritdoc/>
    public async Task FlushDirtyPersistenceAsync(CancellationToken ct = default)
    {
        List<PersistedChunkDelta> chunkBatch = [];
        List<CharacterRecord> characterBatch = [];
        DateTimeOffset now = _time.GetUtcNow();

        lock (_gate)
        {
            if (_dirtyChunks.Count > 0)
            {
                foreach (long key in _dirtyChunks)
                {
                    (int cx, int cy) = UnpackKey(key);
                    if (_chunkWorld.TryGetDelta(cx, cy, out ChunkDelta? delta) && delta is not null)
                    {
                        chunkBatch.Add(new PersistedChunkDelta(
                            cx,
                            cy,
                            delta.Revision,
                            delta.GeneratorVersion,
                            delta.Serialize(),
                            now));
                    }
                }

                _dirtyChunks.Clear();
            }

            foreach (KeyValuePair<ulong, ActivePlayerState> kvp in _playersByConnection)
            {
                ActivePlayerState state = kvp.Value;
                bool movedSinceSave = _simulation.Entities.IsAlive(state.Entity) &&
                    _simulation.Entities.HasTransform(state.Entity) &&
                    Vector2.DistanceSquared(_simulation.Entities.GetTransform(state.Entity).Position, state.LastSavedPosition) >= 16f;

                if (state.PersistenceDirty || state.InventoryDirty || movedSinceSave)
                {
                    CharacterRecord updated = this.BuildUpdatedRecordLocked(state, serializeInventory: state.InventoryDirty);
                    state.SourceCharacter = updated;
                    state.LastSavedPosition = new Vector2(updated.PositionX, updated.PositionY);
                    state.PersistenceDirty = false;
                    state.InventoryDirty = false;
                    _dirtyCharacters[updated.Id] = updated;
                }
            }

            if (_dirtyCharacters.Count > 0)
            {
                foreach (KeyValuePair<Guid, CharacterRecord> kvp in _dirtyCharacters)
                {
                    characterBatch.Add(kvp.Value);
                }

                _dirtyCharacters.Clear();
            }
        }

        if (chunkBatch.Count > 0 || characterBatch.Count > 0)
        {
            try
            {
                if (chunkBatch.Count > 0)
                {
                    await _chunkDeltaRepo.SaveBatchAsync(chunkBatch, ct).ConfigureAwait(false);
                }

                if (characterBatch.Count > 0)
                {
                    await _characterStore.SaveBatchAsync(characterBatch, ct).ConfigureAwait(false);
                }
            }
            catch
            {
                lock (_gate)
                {
                    for (int i = 0; i < chunkBatch.Count; i++)
                    {
                        _ = _dirtyChunks.Add(Chunk2D.PackKey(chunkBatch[i].ChunkX, chunkBatch[i].ChunkY));
                    }

                    for (int i = 0; i < characterBatch.Count; i++)
                    {
                        CharacterRecord failed = characterBatch[i];
                        _ = _dirtyCharacters.TryAdd(failed.Id, failed);
                    }
                }

                throw;
            }
        }
    }

    private void DispatchPendingTickOutputs()
    {
        for (int i = 0; i < _dispatchSpawns.Count; i++)
        {
            OutboundSpawn item = _dispatchSpawns[i];
            EntitySpawnSnapshot snap = item.Snapshot;
            _outboundSink.SendEntitySpawn(item.ConnectionId, in snap);
        }

        for (int i = 0; i < _dispatchDespawns.Count; i++)
        {
            OutboundDespawn item = _dispatchDespawns[i];
            _outboundSink.SendEntityDespawn(item.ConnectionId, item.NetworkId, item.ServerTick);
        }

        Span<EntityDeltaEntry> deltaSpan = CollectionsMarshal.AsSpan(_dispatchDeltaEntries);
        for (int i = 0; i < _dispatchDeltas.Count; i++)
        {
            OutboundDelta item = _dispatchDeltas[i];
            _outboundSink.SendEntityDeltaBatch(
                item.ConnectionId,
                item.ServerTick,
                item.LastClientTick,
                item.SelfX,
                item.SelfY,
                deltaSpan.Slice(item.Offset, item.Count));
        }

        Span<ulong> recipientsSpan = CollectionsMarshal.AsSpan(_dispatchRecipients);
        for (int i = 0; i < _dispatchCombatHits.Count; i++)
        {
            OutboundCombatHit hit = _dispatchCombatHits[i];
            _outboundSink.SendCombatHit(
                recipientsSpan.Slice(hit.RecipientsOffset, hit.RecipientsCount),
                hit.AttackerNetworkId,
                hit.DefenderNetworkId,
                hit.Damage,
                hit.RemainingHealth,
                hit.IsCritical,
                hit.IsKilled);
        }

        for (int i = 0; i < _dispatchSlotChanges.Count; i++)
        {
            OutboundSlotChange change = _dispatchSlotChanges[i];
            ItemSlot slotCopy = change.Slot;
            _outboundSink.SendInventorySlotChanged(
                change.ConnectionId,
                change.SlotIndex,
                slotCopy.ItemId,
                slotCopy.Quantity,
                slotCopy.Durability,
                ItemEnhancementPolicy.GetUpgradeLevel(in slotCopy));
        }

        for (int i = 0; i < _dispatchPropChanges.Count; i++)
        {
            OutboundPropChange prop = _dispatchPropChanges[i];
            _outboundSink.SendPropChanged(
                recipientsSpan.Slice(prop.RecipientsOffset, prop.RecipientsCount),
                prop.TileX,
                prop.TileY,
                prop.Revision,
                prop.Durability,
                prop.IsDestroyed);
        }

        for (int i = 0; i < _dispatchStructureChanges.Count; i++)
        {
            OutboundStructureChange st = _dispatchStructureChanges[i];
            _outboundSink.SendStructureChanged(
                recipientsSpan.Slice(st.RecipientsOffset, st.RecipientsCount),
                st.TileX,
                st.TileY,
                st.StructureType,
                st.Durability,
                st.DoorState,
                st.Connectivity);
        }

        for (int i = 0; i < _dispatchEquipmentChanges.Count; i++)
        {
            OutboundEquipmentChange eq = _dispatchEquipmentChanges[i];
            _outboundSink.SendEntityEquipmentChanged(
                recipientsSpan.Slice(eq.RecipientsOffset, eq.RecipientsCount),
                eq.NetworkId,
                eq.HeadId,
                eq.ChestId,
                eq.LegsId,
                eq.FeetId,
                eq.WeaponId,
                eq.WeaponUpgrade,
                eq.Level,
                eq.MaxHealth);
        }

        for (int i = 0; i < _dispatchStatsSyncs.Count; i++)
        {
            OutboundStatsSync sync = _dispatchStatsSyncs[i];
            PlayerStatsSnapshot statsSnap = sync.Snapshot;
            _outboundSink.SendPlayerStatsSync(sync.ConnectionId, in statsSnap);
        }

        for (int i = 0; i < _dispatchSkillCasts.Count; i++)
        {
            OutboundSkillCast cast = _dispatchSkillCasts[i];
            _outboundSink.SendSkillCastBroadcast(
                recipientsSpan.Slice(cast.RecipientsOffset, cast.RecipientsCount),
                cast.CasterNetworkId,
                cast.AbilitySlot,
                cast.OriginX,
                cast.OriginY,
                cast.AimX,
                cast.AimY);
        }

        _dispatchSpawns.Clear();
        _dispatchDespawns.Clear();
        _dispatchDeltas.Clear();
        _dispatchDeltaEntries.Clear();
        _dispatchCombatHits.Clear();
        _dispatchSlotChanges.Clear();
        _dispatchPropChanges.Clear();
        _dispatchStructureChanges.Clear();

        for (int i = 0; i < _dispatchConstructionChanges.Count; i++)
        {
            OutboundConstructionChange cc = _dispatchConstructionChanges[i];
            _outboundSink.SendConstructionChanged(
                recipientsSpan.Slice(cc.RecipientsOffset, cc.RecipientsCount),
                cc.TileX,
                cc.TileY,
                cc.StructureType,
                cc.Rotation,
                cc.Stage,
                cc.Size,
                cc.Progress,
                cc.IsDeconstruction);
        }

        _dispatchConstructionChanges.Clear();
        _dispatchEquipmentChanges.Clear();
        _dispatchStatsSyncs.Clear();
        _dispatchSkillCasts.Clear();
        _dispatchRecipients.Clear();
    }

    private void ProcessAiAttacksLocked()
    {
        ReadOnlySpan<AiAttackReadyEvent> attackEvents = _simulation.ReadEvents<AiAttackReadyEvent>();
        for (int i = 0; i < attackEvents.Length; i++)
        {
            AiAttackReadyEvent evt = attackEvents[i];
            if (!_simulation.Entities.IsAlive(evt.Attacker) ||
                !_simulation.Entities.IsAlive(evt.Target) ||
                !_simulation.Entities.HasTransform(evt.Attacker) ||
                !_simulation.Entities.HasTransform(evt.Target) ||
                !_simulation.Entities.HasHealth(evt.Target) ||
                !_simulation.TryGetNetworkIdentity(evt.Attacker, out NetworkIdentity attackerNet) ||
                !_simulation.TryGetNetworkIdentity(evt.Target, out NetworkIdentity targetNet))
            {
                continue;
            }

            Vector2 atkPos = _simulation.Entities.GetTransform(evt.Attacker).Position;
            Vector2 defPos = _simulation.Entities.GetTransform(evt.Target).Position;
            bool hasMeta = _actorsByNetworkId.TryGetValue(attackerNet.NetworkId, out NpcActorMeta atkMeta);
            bool isNight = WorldProgression.IsNightHour(WorldProgression.ComputeTimeOfDayHours(_currentTick, this.TickRate));
            float rawDamage = (hasMeta ? atkMeta.AttackDamage : 10f) * (isNight ? 1.25f : 1f);

            if (hasMeta && atkMeta.ProjectileSpeed > 0f && atkMeta.AttackRange > 64f)
            {
                float maxShotRange = atkMeta.AttackRange * 1.35f;
                Vector2 toTarget = defPos - atkPos;
                float distSq = toTarget.LengthSquared();
                if (distSq > (maxShotRange * maxShotRange) || !this.HasLineOfSightLocked(atkPos, defPos))
                {
                    continue;
                }

                Vector2 shotDir = distSq > 1e-4f ? Vector2.Normalize(toTarget) : Vector2.UnitY;
                this.CollectNearbyConnectionsAroundWorldLocked(atkPos, _aoiRecipientsScratch);
                if (_aoiRecipientsScratch.Count > 0)
                {
                    int castOffset = _flatRecipients.Count;
                    for (int rIdx = 0; rIdx < _aoiRecipientsScratch.Count; rIdx++)
                    {
                        _flatRecipients.Add(_aoiRecipientsScratch[rIdx]);
                    }

                    _pendingSkillCasts.Add(new OutboundSkillCast(
                        castOffset,
                        _aoiRecipientsScratch.Count,
                        attackerNet.NetworkId,
                        AbilityInputEncoding.EncodeMonsterProjectileCast(atkMeta.ProjectileArchetype),
                        atkPos.X,
                        atkPos.Y,
                        defPos.X,
                        defPos.Y));
                }

                uint lifeTicks = (uint)Math.Max(14, MathF.Ceiling(maxShotRange / atkMeta.ProjectileSpeed * this.TickRate));
                this.SpawnProjectileLocked(
                    attackerNet.NetworkId,
                    ownerConnectionId: 0UL,
                    fromPlayer: false,
                    atkPos + (shotDir * 14f),
                    shotDir * atkMeta.ProjectileSpeed,
                    atkMeta.ProjectileArchetype,
                    rawDamage,
                    isCritical: false,
                    lifeTicks);
                continue;
            }

            float meleeHitRange = MathF.Max(60f, atkMeta.AttackRange + 16f);
            if (Vector2.DistanceSquared(atkPos, defPos) > (meleeHitRange * meleeHitRange) || !this.HasLineOfSightLocked(atkPos, defPos))
            {
                continue;
            }

            this.CollectNearbyConnectionsAroundWorldLocked(atkPos, _aoiRecipientsScratch);
            if (_aoiRecipientsScratch.Count > 0)
            {
                int castOffset = _flatRecipients.Count;
                for (int rIdx = 0; rIdx < _aoiRecipientsScratch.Count; rIdx++)
                {
                    _flatRecipients.Add(_aoiRecipientsScratch[rIdx]);
                }

                _pendingSkillCasts.Add(new OutboundSkillCast(
                    castOffset,
                    _aoiRecipientsScratch.Count,
                    attackerNet.NetworkId,
                    AbilityInputEncoding.EncodeMonsterMeleeCast(atkMeta.ArchetypeId),
                    atkPos.X,
                    atkPos.Y,
                    defPos.X,
                    defPos.Y));
            }

            ActivePlayerState? victim = null;
            if (_connectionByNetworkId.TryGetValue(targetNet.NetworkId, out ulong targetConn))
            {
                _ = _playersByConnection.TryGetValue(targetConn, out victim);
            }

            this.ApplyDamageToPlayerLocked(victim, attackerNet.NetworkId, evt.Target, targetNet.NetworkId, defPos, rawDamage);
        }

        _simulation.ClearEvents<AiAttackReadyEvent>();
    }

    private void ApplyDamageToPlayerLocked(
        ActivePlayerState? victim,
        ulong attackerNetId,
        Entity targetEntity,
        ulong targetNetId,
        Vector2 defPos,
        float rawDamage)
    {
        if (!_simulation.Entities.IsAlive(targetEntity) || !_simulation.Entities.HasHealth(targetEntity))
        {
            return;
        }

        HealthComponent hp = _simulation.Entities.GetHealth(targetEntity);
        float monsterDamage = rawDamage;
        if (victim is not null)
        {
            monsterDamage = CharacterProgressionPolicy.MitigateDamage(rawDamage, victim.Stats.Defense);
            victim.LastCombatDamageTick = _currentTick;
            victim.StatsDirty = true;
            this.DegradeEquippedArmorLocked(victim, 0.5f);
        }

        float nextHp = MathF.Max(0f, hp.Current - monsterDamage);
        bool killed = nextHp <= 0f;

        if (killed)
        {
            Vector2 sanctuaryPos = _chunkWorld.SpawnWorldPosition;
            _simulation.Entities.SetHealth(targetEntity, new HealthComponent(hp.Max, hp.Max));
            _simulation.EnqueueCommand(SimulationCommand.Teleport(targetEntity, sanctuaryPos));
            if (_simulation.Entities.HasVelocity(targetEntity))
            {
                _simulation.Entities.SetVelocity(targetEntity, new Velocity2D(0f, 0f, 5f));
            }

            if (victim is not null)
            {
                victim.CurrentMana = victim.Stats.MaxMana;
                victim.CurrentStamina = victim.Stats.MaxStamina;
                victim.StatsDirty = true;
                this.MarkCharacterDirtyLocked(victim, inventoryChanged: false);
            }

            this.ClearAggroAgainstTargetLocked(targetEntity, defPos);
        }
        else
        {
            _simulation.Entities.SetHealth(targetEntity, new HealthComponent(nextHp, hp.Max));
        }

        _simulation.MarkDirty(targetEntity, EntityDirtyMask.Health);
        this.QueueCombatHitLocked(defPos, attackerNetId, targetNetId, monsterDamage, killed ? hp.Max : nextHp, false, killed);
    }

    private void ClearAggroAgainstTargetLocked(Entity victimEntity, Vector2 deathPos)
    {
        _simulation.OverlapCircle(deathPos, 480f, _combatHitScratch, out int count, MonsterLayer);
        EntityAttachmentStore<ThreatTable> threatStore = _simulation.Entities.GetAttachmentStore<ThreatTable>();

        for (int i = 0; i < count; i++)
        {
            Entity mob = _combatHitScratch[i];
            if (threatStore.TryGet(mob, out ThreatTable? table) && table is not null)
            {
                table.Clear();
            }

            if (_simulation.Entities.HasComponent<AiBrainComponent>(mob))
            {
                ref AiBrainComponent brain = ref _simulation.Entities.GetComponent<AiBrainComponent>(mob);
                if (brain.TargetEntity == victimEntity)
                {
                    brain = brain with
                    {
                        State = AiState.ReturnHome,
                        TargetEntity = Entity.Null,
                        WindupRemainingTicks = 0,
                        RepathCooldownTicks = 0,
                    };
                }
            }
        }
    }

    private void ProcessMonsterRespawnsLocked()
    {
        for (int i = _pendingRespawns.Count - 1; i >= 0; i--)
        {
            PendingMonsterRespawn pending = _pendingRespawns[i];
            if (_currentTick < pending.RespawnTick)
            {
                continue;
            }

            _pendingRespawns.RemoveAt(i);
            this.SpawnActorFromMetaLocked(pending.Meta);
        }
    }

    private void ProcessDespawnedEntitiesLocked()
    {
        ReadOnlySpan<EntityDespawnedEvent> despawns = _simulation.ReadEvents<EntityDespawnedEvent>();
        for (int i = 0; i < despawns.Length; i++)
        {
            ulong netId = despawns[i].NetworkId;
            if (netId != 0UL)
            {
                _ = _lootByNetworkId.Remove(netId);
                _ = _actorsByNetworkId.Remove(netId);
                _ = _projectilesByNetworkId.Remove(netId);
            }
        }

        _simulation.ClearEvents<EntityDespawnedEvent>();
        this.CleanupExpiredLootMetaLocked();
    }

    private void CollectNearbyConnectionsLocked(int tileX, int tileY, List<ulong> output)
    {
        Vector2 target = new((tileX + 0.5f) * this.TileSize, (tileY + 0.5f) * this.TileSize);
        this.CollectNearbyConnectionsAroundWorldLocked(target, output);
    }

    private void CollectNearbyConnectionsAroundWorldLocked(Vector2 target, List<ulong> output)
    {
        output.Clear();
        float radiusSq = (_aoi.ViewRadius + (this.TileSize * 2f)) * (_aoi.ViewRadius + (this.TileSize * 2f));
        foreach (KeyValuePair<ulong, ActivePlayerState> kvp in _playersByConnection)
        {
            ActivePlayerState state = kvp.Value;
            if (!_simulation.Entities.HasTransform(state.Entity))
            {
                continue;
            }

            if (Vector2.DistanceSquared(_simulation.Entities.GetTransform(state.Entity).Position, target) <= radiusSq)
            {
                output.Add(kvp.Key);
            }
        }
    }
}
