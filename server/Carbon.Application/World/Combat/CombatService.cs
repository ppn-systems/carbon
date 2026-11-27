// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.AI.Components;
using Carbon.AI.Tactics;
using Carbon.AI.Threat;
using Carbon.Application.World.Players;
using Carbon.Application.World.Replication;
using Carbon.Combat.Damage;
using Carbon.Items.Progression;
using Carbon.Physics.Obstacles;
using Carbon.Physics.Shapes;
using Carbon.Primitives.Random;
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
    private readonly Func<ulong> _currentTick;
    private readonly List<ulong> _recipients = new(64);
    private readonly Entity[] _packAggroScratch = new Entity[16];

    /// <summary>Initializes a new instance of the <see cref="CombatService"/> class.</summary>
    public CombatService(
        WorldSimulation simulation,
        IWorldPhysics2D worldPhysics,
        uint monsterLayer,
        OutboundTickBuffer outbound,
        Action<Vector2, List<ulong>> collectNearbyConnections,
        MonsterKilledHandler onMonsterKilled,
        Func<ulong> currentTick)
    {
        _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
        _worldPhysics = worldPhysics ?? throw new ArgumentNullException(nameof(worldPhysics));
        _monsterLayer = monsterLayer;
        _outbound = outbound ?? throw new ArgumentNullException(nameof(outbound));
        _collectNearbyConnections = collectNearbyConnections ?? throw new ArgumentNullException(nameof(collectNearbyConnections));
        _onMonsterKilled = onMonsterKilled ?? throw new ArgumentNullException(nameof(onMonsterKilled));
        _currentTick = currentTick ?? throw new ArgumentNullException(nameof(currentTick));
    }

    /// <summary>
    /// Rolls a critical hit from <paramref name="critChance"/>: a keyed PRF of the secret roll key, the tick, the attacker
    /// and its nonce (ADR-026), so a client that knows the world seed, the tick and its nonce cannot predict it.
    /// </summary>
    /// <param name="critChance">Crit probability.</param>
    /// <param name="key">Secret roll key of the tick's epoch.</param>
    /// <param name="tick">Simulation tick.</param>
    /// <param name="attackerId">Attacker network id.</param>
    /// <param name="nonce">Attacker's monotonic roll counter.</param>
    /// <returns>Whether the hit crits.</returns>
    public static bool RollCriticalHit(float critChance, in GameplayKey key, ulong tick, ulong attackerId, uint nonce) =>
        CombatRoll.Roll(critChance, key, CombatRoll.CriticalDomain, tick, attackerId, nonce);

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

            // One damage rule for every path (audit DOM-1): invulnerable targets refuse the hit, monster armor mitigates it.
            if (!CombatDamage.TryResolve(_simulation, target, damage, default(ArmorDamageMitigation), out float dealt))
            {
                continue;
            }

            anyDamaged = true;
            // Dealing damage is combat too: it pauses out-of-combat regeneration like taking a hit does.
            attacker.LastCombatDamageTick = _currentTick();
            HealthComponent hp = _simulation.Entities.GetHealth(target);

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
                    if (brain.State != AiState.Flee)
                    {
                        brain = brain with { State = AiState.Chase, TargetEntity = attacker.Entity, RepathCooldownTicks = 0 };
                    }
                }

                if (_simulation.Entities.HasVelocity(target) && knockbackSpeed > 0f)
                {
                    ref Velocity2D targetVel = ref _simulation.Entities.GetVelocity(target);
                    targetVel.Linear = aimDir * knockbackSpeed;
                    _simulation.MarkDirty(target, EntityDirtyMask.Velocity);
                }

                // Pack social aggro: data-driven packs (monsters.json behavior.pack) alert same-pack allies in their
                // assist radius; legacy monsters without a pack alert any monster within 160px.
                if (AiPack.HasPack(_simulation.Entities, target))
                {
                    _ = AiPack.CallForHelp(_simulation, target, targetPos, attacker.Entity, dealt * 0.8f, 1f, _packAggroScratch);
                }
                else
                {
                    this.AlertNearbyMonsters(target, targetPos, attacker.Entity, dealt * 0.8f, threatStore);
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

    private void AlertNearbyMonsters(Entity target, Vector2 targetPos, Entity attacker, float threat, EntityAttachmentStore<ThreatTable> threatStore)
    {
        _simulation.OverlapCircle(targetPos, 160f, _packAggroScratch, out int allyCount, _monsterLayer);
        for (int a = 0; a < allyCount; a++)
        {
            Entity ally = _packAggroScratch[a];
            if (ally == target || !_simulation.Entities.IsAlive(ally) || !_simulation.Entities.HasComponent<AiBrainComponent>(ally))
            {
                continue;
            }

            threatStore.GetOrAdd(ally, static () => new ThreatTable()).AddThreat(attacker, threat);
            ref AiBrainComponent allyBrain = ref _simulation.Entities.GetComponent<AiBrainComponent>(ally);
            if (allyBrain.State is AiState.Idle or AiState.Patrol)
            {
                allyBrain = allyBrain with { State = AiState.Chase, TargetEntity = attacker, RepathCooldownTicks = 0 };
            }
        }
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
        bool isKilled,
        ushort statusEffectId = 0,
        byte statusEffectLevel = 0)
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
            isKilled,
            statusEffectId,
            statusEffectLevel));
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
