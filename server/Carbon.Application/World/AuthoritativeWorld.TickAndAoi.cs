// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.Persistence;
using Carbon.Application.World.Players;
using Carbon.Application.World.Replication;
using Carbon.AI.Components;
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

            _currentTick++;
            float dt = 1f / this.TickRate;

            // ADR-017: mutating requests submitted since the last tick run first, in arrival order.
            _commandExecutor.Drain(_commands);

            foreach (KeyValuePair<ulong, PlayerState> kvp in _players.ByConnection)
            {
                PlayerState state = kvp.Value;
                bool isMoving;
                bool isSprinting = false;
                if (state.HasPendingInput)
                {
                    isMoving = state.PendingDirection.LengthSquared() > 1e-4f;
                    if (isMoving)
                    {
                        this.WakeLocked(state);
                    }
                    isSprinting = isMoving && state.PendingSprint && state.CurrentStamina > 2f;
                    if (isSprinting)
                    {
                        state.CurrentStamina = MathF.Max(0f, state.CurrentStamina - (22f * dt));
                        state.StatsDirty = true;
                    }

                    float speed = (isSprinting ? SprintSpeed : BaseWalkSpeed) * state.Stats.MoveSpeedMultiplier * _needs.GetMoveScale(state.Entity);
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

                this.PrepareNeedsForTickLocked(state, isSprinting);

                if (state.PendingPickupLootId != 0UL)
                {
                    _ = _inventory.TryPickup(state, state.PendingPickupLootId, 84f * 84f);
                    state.PendingPickupLootId = 0UL;
                }
            }

            _creatures.Spawner.TimeOfDayHours = WorldProgression.ComputeTimeOfDayHours(_currentTick, this.TickRate);
            _simulation.Tick(new FixedTickArgs(_currentTick, dt, _currentTick * dt));
            this.ProcessSurvivalEventsLocked();
            if ((_currentTick % this.TickRate) == 0UL)
            {
                this.AdvanceFoodSpoilageLocked(SpoilageStepSeconds);
            }
            this.BindSpawnedMonstersLocked();
            this.QueueAttackPhasesLocked();

            _projectiles.Step();

            foreach (KeyValuePair<ulong, PlayerState> kvp in _players.ByConnection)
            {
                PlayerState state = kvp.Value;
                if (state.HasPendingAttack)
                {
                    this.ProcessPlayerAttackLocked(state);
                    state.HasPendingAttack = false;
                }
            }

            this.ProcessBuildJobsLocked(dt);
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
                            _outbound.Pending.PropChanges.Add(new OutboundPropChange(
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

            foreach (KeyValuePair<ulong, PlayerState> kvp in _players.ByConnection)
            {
                PlayerState observer = kvp.Value;
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

                EntityDirtyMask selfDirty = _replication.ReplicateTo(
                    observer.Session.ConnectionId,
                    observer.Session.PlayerNetworkId,
                    observer.Entity,
                    obsPos,
                    observer.LastClientTick,
                    _currentTick);
                if ((selfDirty & EntityDirtyMask.Transform) != 0)
                {
                    observer.PersistenceDirty = true;
                }

                // Out-of-combat natural HP regeneration (+3 HP/s after 6s without taking combat hits).
                if ((_currentTick % this.TickRate) == 0UL &&
                    (_currentTick - observer.LastCombatDamageTick) >= (ulong)(this.TickRate * 6) &&
                    !_needs.IsHealthRegenBlocked(observer.Entity) &&
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
                            !_itemRules.Items.TryGet(curSlot.ItemId, out ItemArchetype? itemDef))
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
                        _outbound.Pending.SlotChanges.Add(new OutboundSlotChange(observer.Session.ConnectionId, (ushort)sIdx, repaired));
                        this.MarkCharacterDirtyLocked(observer, inventoryChanged: true);
                    }
                }

                // Auto-collect nearby stackable/bag ground loot within 32px after brief drop grace period.
                _inventory.CollectNearbyGroundLoot(observer, obsPos);

                if (observer.StatsDirty || (_currentTick % 10UL) == 0UL)
                {
                    this.EnqueueStatsSyncLocked(observer, observer.LeveledUpPending);
                    observer.StatsDirty = false;
                }
            }

            _replication.ClearDirtyFlags();

            this.SwapOutboundBuffersLocked();
        }

        this.DispatchPendingTickOutputs();
    }

    private void SwapOutboundBuffersLocked() => _outbound.Swap();

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

            foreach (KeyValuePair<ulong, PlayerState> kvp in _players.ByConnection)
            {
                PlayerState state = kvp.Value;
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
                    _inFlightCharacters[kvp.Key] = kvp.Value;
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

                lock (_gate)
                {
                    this.ReleaseInFlightCharactersLocked(characterBatch);
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

                    this.ReleaseInFlightCharactersLocked(characterBatch);
                }

                throw;
            }
        }
    }

    /// <summary>
    /// Drops the in-flight entries of a finished save. An entry replaced by a newer flush meanwhile is kept.
    /// </summary>
    private void ReleaseInFlightCharactersLocked(List<CharacterRecord> batch)
    {
        for (int i = 0; i < batch.Count; i++)
        {
            CharacterRecord saved = batch[i];
            if (_inFlightCharacters.TryGetValue(saved.Id, out CharacterRecord? current) && ReferenceEquals(current, saved))
            {
                _ = _inFlightCharacters.Remove(saved.Id);
            }
        }
    }

    private void DispatchPendingTickOutputs() => _outbound.Dispatch(_outboundSink);

    private void ApplyDamageToPlayerLocked(
        PlayerState? victim,
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
            this.InterruptMealOnDamageLocked(victim);
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
                this.ResetNeedsAfterDeathLocked(victim);
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

    private void ProcessDespawnedEntitiesLocked()
    {
        ReadOnlySpan<EntityDespawnedEvent> despawns = _simulation.ReadEvents<EntityDespawnedEvent>();
        for (int i = 0; i < despawns.Length; i++)
        {
            ulong netId = despawns[i].NetworkId;
            if (netId != 0UL)
            {
                _groundLoot.Forget(netId);
                _ = _actorsByNetworkId.Remove(netId);
                _projectiles.Forget(netId);
            }
        }

        _simulation.ClearEvents<EntityDespawnedEvent>();
        _groundLoot.ForgetDeadStacks();
    }

    private void CollectNearbyConnectionsLocked(int tileX, int tileY, List<ulong> output)
    {
        Vector2 target = new((tileX + 0.5f) * this.TileSize, (tileY + 0.5f) * this.TileSize);
        this.CollectNearbyConnectionsAroundWorldLocked(target, output);
    }

    private void CollectNearbyConnectionsAroundWorldLocked(Vector2 target, List<ulong> output)
    {
        output.Clear();
        float radiusSq = (_replication.ViewRadius + (this.TileSize * 2f)) * (_replication.ViewRadius + (this.TileSize * 2f));
        foreach (KeyValuePair<ulong, PlayerState> kvp in _players.ByConnection)
        {
            PlayerState state = kvp.Value;
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
