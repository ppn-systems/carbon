// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Abstractions.World;
using Carbon.Application.World.Replication;
using Carbon.AI.Components;
using Carbon.AI.Systems;
using Carbon.AI.Threat;
using Carbon.Combat.Attacks;
using Carbon.Contracts.Entities;
using Carbon.Creatures;
using Carbon.Creatures.Components;
using Carbon.Creatures.Definitions;
using Carbon.Creatures.Events;
using Carbon.Creatures.Spawning;
using Carbon.Items.Catalog;
using Carbon.Maps.Data;
using Carbon.Maps.Streaming;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.World;

namespace Carbon.Application.World;

/// <summary>
/// Data-driven creature integration: <see cref="CreatureSystems"/> spawns monsters from <c>resources/config</c>, runs
/// their attack timelines, and this partial binds them to network identities, routes hits through the authoritative
/// player damage pipeline, and broadcasts attack phases (ADR-013).
/// </summary>
public sealed partial class AuthoritativeWorld
{
    private static CreatureSystems RegisterCreatureSystems(
        WorldSimulation simulation,
        CreatureContent content,
        ChunkWorld2D chunkWorld,
        ulong worldSeed,
        float tileSize,
        IAttackEffects effects)
    {
        if (content.Spawning.MonsterLayer != MonsterLayer || content.Spawning.TargetLayerMask != PlayerLayer)
        {
            throw new ArgumentException(
                $"spawning.json monsterLayer/targetLayerMask must be {MonsterLayer}/{PlayerLayer} to match the server collision layers.",
                nameof(content));
        }

        return CreatureSystems.Register(
            simulation,
            content.Catalog,
            content.Spawning,
            new ChunkWorldSpawnSurface(chunkWorld),
            chunkWorld.SpawnWorldPosition,
            worldSeed,
            new AiBehaviorSystem(chunkWorld.NavGrid, tileSize),
            effects);
    }

    private void BindSpawnedMonstersLocked()
    {
        ReadOnlySpan<MonsterSpawnedEvent> spawned = _simulation.ReadEvents<MonsterSpawnedEvent>();
        for (int i = 0; i < spawned.Length; i++)
        {
            MonsterSpawnedEvent evt = spawned[i];
            if (!_simulation.Entities.IsAlive(evt.Monster))
            {
                continue;
            }

            MonsterDefinition def = _creatureCatalog.GetDefinition(evt.MonsterIndex);
            ulong netId = ++_nextNetworkId;
            _simulation.BindNetworkIdentity(evt.Monster, netId);
            _ = _simulation.Entities.GetAttachmentStore<ThreatTable>().GetOrAdd(evt.Monster, static () => new ThreatTable());

            int tx = (int)MathF.Floor(evt.Position.X / this.TileSize);
            int ty = (int)MathF.Floor(evt.Position.Y / this.TileSize);
            long chunkKey = Chunk2D.PackKey(Chunk2D.ToChunkCoord(tx), Chunk2D.ToChunkCoord(ty));

            // Loot is rolled from loot.json (def.LootTableId); the catalog drop only backs worlds without an economy.
            MonsterArchetypeSpec legacyDrop = WorldActorCatalog.GetMonsterSpec(def.ArchetypeId);
            HealthComponent hp = _simulation.Entities.GetHealth(evt.Monster);
            AiBrainComponent brain = _simulation.Entities.GetComponent<AiBrainComponent>(evt.Monster);
            MonsterComponent monster = _simulation.Entities.GetComponent<MonsterComponent>(evt.Monster);

            _actorsByNetworkId[netId] = new NpcActorMeta(
                EntityKind.Monster,
                def.ArchetypeId,
                def.DisplayName,
                (ushort)Math.Clamp(evt.Level, 1, ushort.MaxValue),
                evt.Position,
                hp.Max,
                brain.MoveSpeed,
                def.Radius,
                monster.Damage,
                0U,
                chunkKey,
                legacyDrop.DropItemId,
                legacyDrop.DropMinQuantity,
                ComputeExperienceReward(hp.Max, evt.Level, evt.IsElite, evt.IsNight),
                brain.AttackRange,
                LootTableId: def.LootTableId);
        }

        _simulation.ClearEvents<MonsterSpawnedEvent>();
    }

    /// <summary>
    /// Kill XP from the scaled (data-driven) monster: <c>clamp(0.45 * maxHp + 4 * level, 25, 650)</c>, x2 for elites,
    /// x1.35 at night (the formula previously applied by <c>WorldActorCatalog.ScaleMonster</c>).
    /// </summary>
    private static uint ComputeExperienceReward(float maxHealth, int level, bool isElite, bool isNight)
    {
        float xp = Math.Clamp((maxHealth * 0.45f) + (level * 4f), 25f, 650f);
        xp *= isElite ? 2f : 1f;
        xp *= isNight ? 1.35f : 1f;
        return (uint)MathF.Round(xp);
    }

    private void QueueAttackPhasesLocked()
    {
        ReadOnlySpan<AttackPhaseChangedEvent> phases = _simulation.ReadEvents<AttackPhaseChangedEvent>();
        for (int i = 0; i < phases.Length; i++)
        {
            AttackPhaseChangedEvent evt = phases[i];
            if (!_simulation.TryGetNetworkIdentity(evt.Attacker, out NetworkIdentity identity) ||
                !_simulation.Entities.HasTransform(evt.Attacker))
            {
                continue;
            }

            this.CollectNearbyConnectionsAroundWorldLocked(_simulation.Entities.GetTransform(evt.Attacker).Position, _aoiRecipientsScratch);
            if (_aoiRecipientsScratch.Count == 0)
            {
                continue;
            }

            int offset = _outbound.Pending.Recipients.Count;
            _outbound.Pending.Recipients.AddRange(_aoiRecipientsScratch);
            _outbound.Pending.AttackPhases.Add(new OutboundAttackPhase(
                offset,
                _aoiRecipientsScratch.Count,
                new AttackPhaseSnapshot(
                    identity.NetworkId,
                    _currentTick,
                    (byte)evt.Kind,
                    (byte)evt.Phase,
                    (byte)evt.Shape,
                    evt.Interrupted,
                    evt.TargetPosition.X,
                    evt.TargetPosition.Y,
                    evt.Direction.X,
                    evt.Direction.Y,
                    evt.AreaRadius,
                    evt.HalfAngleRadians,
                    (ushort)Math.Min(evt.PhaseDurationTicks, ushort.MaxValue),
                    evt.Progress)));
        }

        _simulation.ClearEvents<AttackPhaseChangedEvent>();
        _simulation.ClearEvents<ProjectileLaunchedEvent>();
    }

    private bool ApplyMonsterHitLocked(Entity attacker, Entity victim, float damage)
    {
        if (!_simulation.TryGetNetworkIdentity(attacker, out NetworkIdentity attackerNet) ||
            !_simulation.TryGetNetworkIdentity(victim, out NetworkIdentity victimNet) ||
            !_simulation.Entities.HasTransform(victim))
        {
            return false;
        }

        ActivePlayerState? victimState = null;
        if (_connectionByNetworkId.TryGetValue(victimNet.NetworkId, out ulong victimConn))
        {
            _ = _playersByConnection.TryGetValue(victimConn, out victimState);
        }

        this.ApplyDamageToPlayerLocked(
            victimState,
            attackerNet.NetworkId,
            victim,
            victimNet.NetworkId,
            _simulation.Entities.GetTransform(victim).Position,
            damage);
        return true;
    }

    private Entity LaunchMonsterProjectileLocked(in ProjectileLaunchRequest request)
    {
        if (!_simulation.TryGetNetworkIdentity(request.Owner, out NetworkIdentity ownerNet))
        {
            return Entity.Null;
        }

        return _projectiles.Spawn(
            ownerNet.NetworkId,
            ownerConnectionId: 0UL,
            fromPlayer: false,
            request.Origin,
            request.Velocity,
            request.VisualArchetype,
            request.Damage,
            isCritical: false,
            request.LifetimeTicks);
    }

    /// <summary>
    /// Server <see cref="IAttackEffects"/>: hits use the authoritative player damage path (mitigation, death,
    /// <c>CombatHit</c>), projectiles are replicated server projectiles. Invoked inside the locked simulation tick.
    /// </summary>
    private sealed class ServerAttackEffects(AuthoritativeWorld owner) : IAttackEffects
    {
        public bool ApplyHit(WorldSimulation world, Entity attacker, Entity victim, float damage) =>
            owner.ApplyMonsterHitLocked(attacker, victim, damage);

        public Entity LaunchProjectile(WorldSimulation world, in ProjectileLaunchRequest request) =>
            owner.LaunchMonsterProjectileLocked(in request);
    }
}
