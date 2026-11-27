// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Physics.Obstacles;
using Carbon.Physics.Shapes;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.World;

namespace Carbon.Application.World.Projectiles;

/// <summary>A server projectile in flight, keyed by its network id.</summary>
internal readonly record struct ProjectileState(
    Entity Entity,
    ulong OwnerNetworkId,
    ulong OwnerConnectionId,
    bool FromPlayer,
    ushort VisualArchetype,
    float Damage,
    bool IsCritical,
    Vector2 Velocity,
    uint RemainingTicks);

/// <summary>Applies the damage of a projectile hit; implemented by the world, which owns players and the damage pipeline.</summary>
internal interface IProjectileHitHandler
{
    /// <summary>A player's projectile hit <paramref name="monster"/> (closest along the swept segment).</summary>
    void OnMonsterHit(in ProjectileState projectile, Entity monster, Vector2 segmentStart, Vector2 shotDirection);

    /// <summary>
    /// Tests a monster projectile's swept segment against players and applies damage to the first one hit.
    /// Returns <see langword="true"/> when a player was hit (the projectile is consumed).
    /// </summary>
    bool TryHitPlayer(in ProjectileState projectile, Vector2 segmentStart, Vector2 segmentEnd);
}

/// <summary>
/// Owns server projectiles (audit SOLID-1, extracted from <see cref="AuthoritativeWorld"/>): spawning, and each tick's
/// step, which ends a projectile on a wall, on its first hit or when its lifetime runs out. Hit detection is swept along
/// the segment the projectile moved this tick; damage is delegated to <see cref="IProjectileHitHandler"/>. Called only
/// from inside the world tick lock.
/// </summary>
internal sealed class ProjectileService
{
    /// <summary>Physics layer of projectile trigger colliders.</summary>
    public const uint ProjectileLayer = 16U;

    private readonly WorldSimulation _simulation;
    private readonly IWorldPhysics2D _worldPhysics;
    private readonly uint _monsterLayer;
    private readonly Func<ulong> _allocateNetworkId;
    private readonly IProjectileHitHandler _hits;
    private readonly Dictionary<ulong, ProjectileState> _byNetworkId = new();
    private readonly List<(ulong NetworkId, ProjectileState State)> _tickScratch = new(32);
    private readonly List<ulong> _expiredScratch = new(32);
    private readonly Entity[] _overlapScratch = new Entity[32];

    /// <summary>Initializes a new instance of the <see cref="ProjectileService"/> class.</summary>
    public ProjectileService(
        WorldSimulation simulation,
        IWorldPhysics2D worldPhysics,
        uint monsterLayer,
        Func<ulong> allocateNetworkId,
        IProjectileHitHandler hits)
    {
        _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
        _worldPhysics = worldPhysics ?? throw new ArgumentNullException(nameof(worldPhysics));
        _monsterLayer = monsterLayer;
        _allocateNetworkId = allocateNetworkId ?? throw new ArgumentNullException(nameof(allocateNetworkId));
        _hits = hits ?? throw new ArgumentNullException(nameof(hits));
    }

    /// <summary>Gets the number of projectiles in flight.</summary>
    public int Count => _byNetworkId.Count;

    /// <summary>Gets the projectile for <paramref name="networkId"/>.</summary>
    public bool TryGet(ulong networkId, out ProjectileState state) => _byNetworkId.TryGetValue(networkId, out state);

    /// <summary>Forgets <paramref name="networkId"/> (its entity was despawned).</summary>
    public void Forget(ulong networkId) => _ = _byNetworkId.Remove(networkId);

    /// <summary>Spawns a replicated projectile entity and starts tracking it.</summary>
    public Entity Spawn(
        ulong ownerNetworkId,
        ulong ownerConnectionId,
        bool fromPlayer,
        Vector2 origin,
        Vector2 velocity,
        ushort visualArchetype,
        float damage,
        bool isCritical,
        uint lifetimeTicks)
    {
        ulong netId = _allocateNetworkId();
        Entity proj = _simulation.SpawnNetworkEntity(netId, origin, 4f);
        _simulation.Entities.SetCollider(proj, new Collider2D(ColliderKind.Circle, 4f, default, true, ProjectileLayer, 0U));
        _simulation.Entities.SetVelocity(proj, new Velocity2D(velocity.X, velocity.Y, 0f));

        _byNetworkId[netId] = new ProjectileState(
            proj,
            ownerNetworkId,
            ownerConnectionId,
            fromPlayer,
            visualArchetype,
            damage,
            isCritical,
            velocity,
            Math.Max(2U, lifetimeTicks));
        return proj;
    }

    /// <summary>Advances every projectile by one tick (walls, hits, lifetime) and despawns the ones that ended.</summary>
    public void Step()
    {
        if (_byNetworkId.Count == 0)
        {
            return;
        }

        _tickScratch.Clear();
        foreach (KeyValuePair<ulong, ProjectileState> kv in _byNetworkId)
        {
            _tickScratch.Add((kv.Key, kv.Value));
        }

        _expiredScratch.Clear();

        for (int i = 0; i < _tickScratch.Count; i++)
        {
            (ulong netId, ProjectileState meta) = _tickScratch[i];
            if (!_simulation.Entities.IsAlive(meta.Entity) || !_simulation.Entities.HasTransform(meta.Entity))
            {
                _expiredScratch.Add(netId);
                continue;
            }

            Vector2 currentPos = _simulation.Entities.GetTransform(meta.Entity).Position;
            Vector2 startPos = _simulation.Entities.TryGetComponent(meta.Entity, out PreviousTransform2D prev)
                ? prev.Position
                : currentPos;
            Vector2 delta = currentPos - startPos;
            float dist = delta.Length();

            if (dist > 0.01f)
            {
                Ray2D wallRay = new(startPos, delta / dist, dist);
                if (_worldPhysics.Raycast(in wallRay, out _))
                {
                    _expiredScratch.Add(netId);
                    continue;
                }
            }

            bool hitEntity = false;
            Vector2 shotDir = meta.Velocity.LengthSquared() > 1e-4f ? Vector2.Normalize(meta.Velocity) : Vector2.UnitX;

            if (meta.FromPlayer)
            {
                Vector2 mid = (startPos + currentPos) * 0.5f;
                _simulation.OverlapCircle(mid, (dist * 0.5f) + 20f, _overlapScratch, out int candidateCount, _monsterLayer);

                Entity closestMob = Entity.Null;
                float bestProjT = float.MaxValue;
                for (int c = 0; c < candidateCount; c++)
                {
                    Entity candidate = _overlapScratch[c];
                    if (!_simulation.Entities.IsAlive(candidate) ||
                        !_simulation.Entities.HasHealth(candidate) ||
                        !_simulation.Entities.HasTransform(candidate))
                    {
                        continue;
                    }

                    Vector2 mobPos = _simulation.Entities.GetTransform(candidate).Position;
                    float mobR = _simulation.Entities.HasCollider(candidate) ? _simulation.Entities.GetCollider(candidate).Radius : 8f;
                    float hitR = mobR + 5f;
                    if (DistancePointToSegmentSq(mobPos, startPos, currentPos, out float segT) <= (hitR * hitR) && segT < bestProjT)
                    {
                        bestProjT = segT;
                        closestMob = candidate;
                    }
                }

                if (closestMob.IsValid)
                {
                    hitEntity = true;
                    _hits.OnMonsterHit(in meta, closestMob, startPos, shotDir);
                }
            }
            else
            {
                hitEntity = _hits.TryHitPlayer(in meta, startPos, currentPos);
            }

            if (hitEntity || meta.RemainingTicks <= 1U)
            {
                _expiredScratch.Add(netId);
            }
            else
            {
                _byNetworkId[netId] = meta with { RemainingTicks = meta.RemainingTicks - 1U };
            }
        }

        for (int i = 0; i < _expiredScratch.Count; i++)
        {
            ulong netId = _expiredScratch[i];
            if (_byNetworkId.Remove(netId, out ProjectileState removed) &&
                _simulation.Entities.IsAlive(removed.Entity))
            {
                _simulation.DespawnEntity(removed.Entity);
            }
        }
    }

    /// <summary>Squared distance from <paramref name="p"/> to segment <paramref name="a"/>-<paramref name="b"/>, with the segment parameter.</summary>
    public static float DistancePointToSegmentSq(Vector2 p, Vector2 a, Vector2 b, out float t)
    {
        Vector2 ab = b - a;
        float lenSq = ab.LengthSquared();
        if (lenSq <= 1e-6f)
        {
            t = 0f;
            return Vector2.DistanceSquared(p, a);
        }

        t = Math.Clamp(Vector2.Dot(p - a, ab) / lenSq, 0f, 1f);
        return Vector2.DistanceSquared(p, a + (ab * t));
    }
}
