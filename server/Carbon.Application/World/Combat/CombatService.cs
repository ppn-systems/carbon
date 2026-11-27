// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.AI.Components;
using Carbon.AI.Threat;
using Carbon.Application.World.Players;
using Carbon.Application.World.Replication;
using Carbon.Combat.Stats;
using Carbon.Items.Progression;
using Carbon.Physics.Obstacles;
using Carbon.Physics.Shapes;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.Networking;
using Carbon.Simulation.World;

namespace Carbon.Application.World.Combat;

/// <summary>Called when a player's hit kills a monster, before its entity is despawned (loot, experience).</summary>
internal delegate void MonsterKilledHandler(PlayerState attacker, ulong monsterNetworkId, Vector2 position);

/// <summary>
/// Player-versus-monster hit resolution and combat broadcasts (audit SOLID-1, extracted from
/// <see cref="AuthoritativeWorld"/>). A hit needs line of sight, is mitigated by the monster's Defense, and either
/// aggroes the monster (threat, chase, knockback, pack assist) or kills it; every hit is broadcast as a
/// <c>CombatHit</c> to nearby players. Called only under the world lock.
/// </summary>
internal sealed class CombatService
{
    private readonly WorldSimulation _simulation;
    private readonly IWorldPhysics2D _worldPhysics;
    private readonly uint _monsterLayer;
    private readonly OutboundTickBuffer _outbound;
    private readonly Action<Vector2, List<ulong>> _collectNearbyConnections;
    private readonly MonsterKilledHandler _onMonsterKilled;
    private readonly List<ulong> _recipients = new(64);
    private readonly Entity[] _packAggroScratch = new Entity[16];

    /// <summary>Initializes a new instance of the <see cref="CombatService"/> class.</summary>
    public CombatService(
        WorldSimulation simulation,
        IWorldPhysics2D worldPhysics,
        uint monsterLayer,
        OutboundTickBuffer outbound,
        Action<Vector2, List<ulong>> collectNearbyConnections,
        MonsterKilledHandler onMonsterKilled)
    {
        _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
        _worldPhysics = worldPhysics ?? throw new ArgumentNullException(nameof(worldPhysics));
        _monsterLayer = monsterLayer;
        _outbound = outbound ?? throw new ArgumentNullException(nameof(outbound));
        _collectNearbyConnections = collectNearbyConnections ?? throw new ArgumentNullException(nameof(collectNearbyConnections));
        _onMonsterKilled = onMonsterKilled ?? throw new ArgumentNullException(nameof(onMonsterKilled));
    }

    /// <summary>Rolls a critical hit from <paramref name="critChance"/> with a deterministic hash of <paramref name="nonce"/>.</summary>
    public static bool RollCriticalHit(float critChance, uint nonce) =>
        CombatRoll.Roll(critChance, nonce, CombatRoll.CriticalSalt);

    /// <summary>
    /// Applies a player's hit of <paramref name="damage"/> to each of <paramref name="targets"/> it can see. Survivors aggro
    /// on the attacker (and alert their pack); killed monsters go through the kill handler and are despawned. Returns
    /// whether any target was damaged.
    /// </summary>
    public bool ApplyMonsterHits(
        PlayerState attacker,
        Vector2 attackerPos,
        Vector2 aimDir,
        ReadOnlySpan<Entity> targets,
        float damage,
        bool isCritical,
        float knockbackSpeed)
    {
        EntityAttachmentStore<ThreatTable> threatStore = _simulation.Entities.GetAttachmentStore<ThreatTable>();
        EntityAttachmentStore<StatSheet> statsStore = _simulation.Entities.GetAttachmentStore<StatSheet>();
        bool anyDamaged = false;

        for (int i = 0; i < targets.Length; i++)
        {
            Entity target = targets[i];
            if (!_simulation.Entities.IsAlive(target) ||
                !_simulation.Entities.HasHealth(target) ||
                !_simulation.TryGetNetworkIdentity(target, out NetworkIdentity targetIdentity))
            {
                continue;
            }

            Vector2 targetPos = _simulation.Entities.HasTransform(target) ? _simulation.Entities.GetTransform(target).Position : attackerPos;
            if (!this.HasLineOfSight(attackerPos, targetPos))
            {
                continue;
            }

            anyDamaged = true;
            HealthComponent hp = _simulation.Entities.GetHealth(target);

            // Monster armor (StatSheet Defense from monsters.json scaling) mitigates player hits like CombatDamage.Apply.
            float dealt = damage;
            if (statsStore.TryGet(target, out StatSheet? targetStats))
            {
                dealt *= 100f / (100f + MathF.Max(0f, targetStats.GetValue(StatType.Defense)));
            }

            float remaining = MathF.Max(0f, hp.Current - dealt);
            bool killed = remaining <= 0f;

            _simulation.Entities.SetHealth(target, new HealthComponent(remaining, hp.Max));
            _simulation.MarkDirty(target, EntityDirtyMask.Health);

            if (!killed)
            {
                threatStore.GetOrAdd(target, static () => new ThreatTable()).AddThreat(attacker.Entity, dealt * 2f);
                if (_simulation.Entities.HasComponent<AiBrainComponent>(target))
                {
                    ref AiBrainComponent brain = ref _simulation.Entities.GetComponent<AiBrainComponent>(target);
                    brain = brain with { State = AiState.Chase, TargetEntity = attacker.Entity, RepathCooldownTicks = 0 };
                }

                if (_simulation.Entities.HasVelocity(target) && knockbackSpeed > 0f)
                {
                    ref Velocity2D targetVel = ref _simulation.Entities.GetVelocity(target);
                    targetVel.Linear = aimDir * knockbackSpeed;
                    _simulation.MarkDirty(target, EntityDirtyMask.Velocity);
                }

                // Pack social aggro: alert nearby allied monsters within 160px to assist.
                _simulation.OverlapCircle(targetPos, 160f, _packAggroScratch, out int allyCount, _monsterLayer);
                for (int a = 0; a < allyCount; a++)
                {
                    Entity ally = _packAggroScratch[a];
                    if (ally == target || !_simulation.Entities.IsAlive(ally) || !_simulation.Entities.HasComponent<AiBrainComponent>(ally))
                    {
                        continue;
                    }

                    threatStore.GetOrAdd(ally, static () => new ThreatTable()).AddThreat(attacker.Entity, dealt * 0.8f);
                    ref AiBrainComponent allyBrain = ref _simulation.Entities.GetComponent<AiBrainComponent>(ally);
                    if (allyBrain.State is AiState.Idle or AiState.Patrol)
                    {
                        allyBrain = allyBrain with { State = AiState.Chase, TargetEntity = attacker.Entity, RepathCooldownTicks = 0 };
                    }
                }
            }
            else
            {
                _onMonsterKilled(attacker, targetIdentity.NetworkId, targetPos);
                _simulation.DespawnEntity(target);
            }

            this.QueueCombatHit(targetPos, attacker.Session.PlayerNetworkId, targetIdentity.NetworkId, dealt, remaining, isCritical, killed);
        }

        return anyDamaged;
    }

    /// <summary>Gets whether no wall stands between <paramref name="from"/> and <paramref name="to"/> (always true within 6px).</summary>
    public bool HasLineOfSight(Vector2 from, Vector2 to)
    {
        Vector2 delta = to - from;
        float dist = delta.Length();
        if (dist <= 6f)
        {
            return true;
        }

        Ray2D ray = new(from, delta / dist, dist - 4f);
        return !_worldPhysics.Raycast(in ray, out _);
    }

    /// <summary>Broadcasts a combat hit (or a heal, as negative damage) to the players near <paramref name="worldPos"/>.</summary>
    public void QueueCombatHit(
        Vector2 worldPos,
        ulong attackerNetId,
        ulong defenderNetId,
        float damage,
        float remainingHealth,
        bool isCritical,
        bool isKilled)
    {
        _collectNearbyConnections(worldPos, _recipients);
        if (_recipients.Count == 0)
        {
            return;
        }

        OutboundBatch pending = _outbound.Pending;
        int offset = pending.Recipients.Count;
        pending.Recipients.AddRange(_recipients);
        pending.CombatHits.Add(new OutboundCombatHit(
            offset,
            _recipients.Count,
            attackerNetId,
            defenderNetId,
            damage,
            remainingHealth,
            isCritical,
            isKilled));
    }

    /// <summary>Broadcasts a skill or projectile cast from <paramref name="origin"/> aimed along <paramref name="aimDir"/>.</summary>
    public void QueueSkillCast(Vector2 origin, Vector2 aimDir, ulong casterNetId, byte abilitySlot)
    {
        _collectNearbyConnections(origin, _recipients);
        if (_recipients.Count == 0)
        {
            return;
        }

        OutboundBatch pending = _outbound.Pending;
        int offset = pending.Recipients.Count;
        pending.Recipients.AddRange(_recipients);
        pending.SkillCasts.Add(new OutboundSkillCast(
            offset,
            _recipients.Count,
            casterNetId,
            abilitySlot,
            origin.X,
            origin.Y,
            origin.X + (aimDir.X * 64f),
            origin.Y + (aimDir.Y * 64f)));
    }
}
