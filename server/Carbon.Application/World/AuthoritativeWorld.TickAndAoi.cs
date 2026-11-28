// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.Persistence;
using Carbon.Application.World.Actors;
using Carbon.Application.World.Players;
using Carbon.Application.World.Replication;
using Carbon.AI.Components;
using Carbon.AI.Threat;
using Carbon.Contracts.Common;
using Carbon.Items.Catalog;
using Carbon.Maps.Generators;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Items.Progression;
using Carbon.Maps.Data;
using Carbon.Maps.Hydrology;
using Carbon.Maps.Props;
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
            _trade.DrainLocked();

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
                    // ADR-033: sprint drains stamina and is off while exhausted.
                    isSprinting = isMoving && state.PendingSprint && _stamina.TrySprint(state, dt);

                    float speed = (isSprinting ? SprintSpeed : BaseWalkSpeed) * state.Stats.MoveSpeedMultiplier * _needs.GetMoveScale(state.Entity)
                        * _statusEffects.GetMoveScale(state.Entity);
                    _simulation.EnqueueCommand(SimulationCommand.MoveInput(
                        state.Entity,
                        state.PendingDirection,
                        speed,
                        state.LastClientTick));
                    state.HasPendingInput = false;
                }

                // ADR-032 on the ADR-033 seam: swimming and diving strokes cost stamina like sprinting.
                bool isSwimStroke = !isSprinting && state.SwimState >= SwimState.Swimming && this.IsPlayerMovingLocked(state) &&
                    _stamina.DrainSwim(state, dt);
                if (!isSprinting && !isSwimStroke)
                {
                    _stamina.Regenerate(state, dt, this.IsPlayerMovingLocked(state), _statusEffects.GetStaminaRegenMultiplier(state.Entity));
                }

                if (state.CurrentMana < state.Stats.MaxMana)
                {
                    state.CurrentMana = MathF.Min(state.Stats.MaxMana, state.CurrentMana + (state.Stats.ManaRegenPerSecond * dt));
                }

                this.PrepareNeedsForTickLocked(state, isSprinting);

                if (state.PendingPickupLootId != 0UL)
                {
                    _ = _inventory.TryPickup(state, state.PendingPickupLootId, 84f * 84f);
                    state.PendingPickupLootId = 0UL;
                }
            }

            _creatures.Spawner.TimeOfDayHours = WorldProgression.ComputeTimeOfDayHours(_currentTick, this.TickRate);
            _creatures.Ai.IsNight = _creatures.Spawner.IsNight;
            _simulation.Tick(new FixedTickArgs(_currentTick, dt, _currentTick * dt));
            this.ProcessStatusPulsesLocked();
            this.ProcessSurvivalEventsLocked();
            _oxygen.Step(_players.ByConnection, dt);
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
                    // ADR-029 D5: a stunned player's attack input is dropped.
                    if (!_statusEffects.IsStunned(state.Entity))
                    {
                        _actions.Attack(state);
                    }

                    state.HasPendingAttack = false;
                }
            }

            this.ProcessBuildJobsLocked(dt);
            this.ProcessDespawnedEntitiesLocked();

            if ((_currentTick % this.TickRate) == 0UL)
            {
                _respawnedPropsScratch.Clear();
                if (_chunkWorld.ProcessPropRespawns(_currentTick, _propRegrowth, this.TickRate, _isPropTileBlocked, _isPropTileWatched, _respawnedPropsScratch) > 0)
                {
                    for (int i = 0; i < _respawnedPropsScratch.Count; i++)
                    {
                        (int rtx, int rty, _, float durability, TreeGrowthStage stage) = _respawnedPropsScratch[i];
                        _ = _dirtyChunks.Add(Chunk2D.PackKey(Chunk2D.ToChunkCoord(rtx), Chunk2D.ToChunkCoord(rty)));
                        (int offset, int count) = this.AppendTileRecipientsLocked(rtx, rty);
                        if (count > 0)
                        {
                            _outbound.Pending.PropChanges.Add(new OutboundPropChange(
                                offset,
                                count,
                                rtx,
                                rty,
                                durability,
                                IsDestroyed: false,
                                stage));
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
                    _chunkWorld.UpdateAroundPosition(obsPos, chunkRadius: StreamChunkRadius, evictionRadius: 8, stream: kvp.Key);
                    _settlements.PopulateAround(obsPos, radius: 2);
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

                // Out-of-combat HP regeneration (progression.json: flat + % of max HP per second) after 6 s without a hit.
                if ((_currentTick % this.TickRate) == 0UL &&
                    (_currentTick - observer.LastCombatDamageTick) >= (ulong)(this.TickRate * 6) &&
                    !_needs.IsHealthRegenBlocked(observer.Entity) &&
                    _simulation.Entities.HasHealth(observer.Entity))
                {
                    HealthComponent php = _simulation.Entities.GetHealth(observer.Entity);
                    if (php.Current > 0f && php.Current < php.Max)
                    {
                        float healed = MathF.Min(php.Max, php.Current + (MathF.Max(0f, observer.Stats.HealthRegenPerSecond) * _statusEffects.GetHealthRegenMultiplier(observer.Entity, _needs.Config)));
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
                if (!observer.IsDead)
                {
                    _inventory.CollectNearbyGroundLoot(observer, obsPos);
                }

                this.QueueStatusEffectSyncLocked(observer);
                _oxygen.QueueSync(observer);

                if (observer.StatsDirty || (_currentTick % 10UL) == 0UL)
                {
                    this.EnqueueStatsSyncLocked(observer, observer.LeveledUpPending);
                    observer.StatsDirty = false;
                }

                _stamina.QueueStatusIfChanged(observer);
            }

            _replication.ClearDirtyFlags();

            this.SwapOutboundBuffersLocked();
        }

        this.DispatchPendingTickOutputs();
    }

    private void SwapOutboundBuffersLocked() => _outbound.Swap();

    /// <inheritdoc/>
    /// <remarks>
    /// Trader sales, chunk deltas and characters are saved independently: a failing store keeps its own rows dirty
    /// for the next flush, but never stops the other stores from saving in this one. The failures are rethrown at
    /// the end (one as-is, several as an <see cref="AggregateException"/>) so the host logs them.
    /// </remarks>
    public async Task FlushDirtyPersistenceAsync(CancellationToken ct = default)
    {
        List<Exception>? failures = null;
        try
        {
            await _trade.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            (failures ??= []).Add(ex);
        }

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
                            delta.Serialize(_currentTick),
                            now));
                    }
                }

                _dirtyChunks.Clear();
            }

            _persistence.CollectBatch(_players.ByConnection.Values, characterBatch);
        }

        if (chunkBatch.Count > 0)
        {
            try
            {
                await _chunkDeltaRepo.SaveBatchAsync(chunkBatch, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                lock (_gate)
                {
                    for (int i = 0; i < chunkBatch.Count; i++)
                    {
                        _ = _dirtyChunks.Add(Chunk2D.PackKey(chunkBatch[i].ChunkX, chunkBatch[i].ChunkY));
                    }
                }

                (failures ??= []).Add(ex);
            }
        }

        if (characterBatch.Count > 0)
        {
            try
            {
                await _characterStore.SaveBatchAsync(characterBatch, ct).ConfigureAwait(false);
                lock (_gate)
                {
                    _persistence.Release(characterBatch);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                lock (_gate)
                {
                    _persistence.Requeue(characterBatch);
                }

                (failures ??= []).Add(ex);
            }
        }

        if (failures is null)
        {
            return;
        }

        ct.ThrowIfCancellationRequested();
        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        throw new AggregateException("Persistence flush failed for more than one store; the failed rows stay dirty for the next flush.", failures);
    }

    private void DispatchPendingTickOutputs() => _outbound.Dispatch(_outboundSink);

    /// <returns>The damage dealt after mitigation; 0 when the hit was avoided or the target cannot be damaged.</returns>
    private float ApplyDamageToPlayerLocked(
        PlayerState? victim,
        ulong attackerNetId,
        Entity targetEntity,
        ulong targetNetId,
        Vector2 defPos,
        float rawDamage,
        DamageKind kind = DamageKind.Physical)
    {
        if (!_simulation.Entities.IsAlive(targetEntity) || !_simulation.Entities.HasHealth(targetEntity) || victim?.IsDead == true)
        {
            return 0f;
        }

        HealthComponent hp = _simulation.Entities.GetHealth(targetEntity);
        float monsterDamage = rawDamage;
        if (victim is not null)
        {
            int attackerLevel = _actorsByNetworkId.TryGetValue(attackerNetId, out NpcActorMeta attackerMeta) ? attackerMeta.Level : victim.Level;
            monsterDamage = _progressionService.ResolveIncomingDamage(victim, rawDamage, kind, attackerLevel, out bool avoided);
            if (avoided)
            {
                // A dodged (or refused) hit deals nothing and does not count as a hit; clients show it as a miss.
                this.QueueCombatHitLocked(defPos, attackerNetId, targetNetId, 0f, hp.Current, false, false);
                return 0f;
            }

            victim.LastCombatDamageTick = _currentTick;
            victim.StatsDirty = true;
            _actions.WearArmor(victim, 0.5f);
            this.InterruptMealOnDamageLocked(victim);
        }

        float nextHp = MathF.Max(0f, hp.Current - monsterDamage);
        bool killed = nextHp <= 0f;

        if (killed && victim is not null)
        {
            // ADR-031: the player stays dead where they fell until they ask to respawn.
            this.KillPlayerLocked(victim, attackerNetId, defPos, DeathCause.Monster);
        }
        else if (killed)
        {
            Vector2 sanctuaryPos = _chunkWorld.SpawnWorldPosition;
            _simulation.Entities.SetHealth(targetEntity, new HealthComponent(hp.Max, hp.Max));
            _simulation.EnqueueCommand(SimulationCommand.Teleport(targetEntity, sanctuaryPos));
            if (_simulation.Entities.HasVelocity(targetEntity))
            {
                _simulation.Entities.SetVelocity(targetEntity, new Velocity2D(0f, 0f, 5f));
            }

            this.ClearAggroAgainstTargetLocked(targetEntity, defPos);
            _statusEffects.Clear(targetEntity);
        }
        else
        {
            _simulation.Entities.SetHealth(targetEntity, new HealthComponent(nextHp, hp.Max));
            if (victim is not null && _actorsByNetworkId.TryGetValue(attackerNetId, out NpcActorMeta hitter) &&
                _simulation.TryGetEntityByNetworkId(attackerNetId, out Entity attackerEntity))
            {
                // ADR-036 D8: the proc level grows with the monster's level (distance from the origin) and with the night now.
                _ = _statusEffects.OnMonsterHit(targetEntity, targetNetId, attackerEntity, attackerNetId, hitter.ArchetypeId, monsterDamage, hp.Max, _currentTick, hitter.Level, _creatures.Spawner.IsNight);
            }
        }

        _simulation.MarkDirty(targetEntity, EntityDirtyMask.Health);
        this.QueueCombatHitLocked(defPos, attackerNetId, targetNetId, monsterDamage, killed && victim is null ? hp.Max : nextHp, false, killed);
        return monsterDamage;
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
