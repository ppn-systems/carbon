// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.AI.Components;
using Carbon.AI.Navigation;
using Carbon.AI.Threat;
using Carbon.Maps.Navigation;
using Carbon.Physics.Obstacles;
using Carbon.Physics.Shapes;
using Carbon.Primitives.Math;
using Carbon.Simulation.Clock;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.ECS.Systems;
using Carbon.Simulation.Networking;
using Carbon.Simulation.World;

namespace Carbon.AI.Systems;

/// <summary>
/// Deterministic finite-state machine behavior system driving <see cref="AiBrainComponent"/> entities through
/// <c>Idle / Patrol -> Chase -> Attack -> ReturnHome (Leash)</c>. Integrates <see cref="ThreatTable"/> aggro,
/// line-of-sight <see cref="WorldSimulation.Raycast"/> checks, and <see cref="PathWaypointBuffer"/> navigation.
/// Executes during <see cref="SimulationPhase.PreUpdate"/>.
/// </summary>
public sealed class AiBehaviorSystem : ISimulationSystem
{
    private const int MaxAggroCandidates = 64;
    private const int MaxSeparationNeighbors = 16;
    private const uint PatrolDefaultWaitTicks = 20;
    private const uint ChaseRepathIntervalTicks = 10;
    private const uint ReturnRepathIntervalTicks = 15;

    private readonly GridPathfinder _pathfinder = new();
    private readonly ITileWalkabilityGrid? _walkabilityGrid;
    private readonly float _tileSize;

    /// <summary>
    /// Initializes a new instance of the <see cref="AiBehaviorSystem"/> class with direct line-of-sight steering.
    /// </summary>
    public AiBehaviorSystem()
        : this(null, 32f)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AiBehaviorSystem"/> class with optional tile-grid A* pathfinding
    /// around walls when line of sight is blocked.
    /// </summary>
    /// <param name="walkabilityGrid">Navigation walkability grid for A* detour pathing.</param>
    /// <param name="tileSize">World dimension of a single navigation tile.</param>
    public AiBehaviorSystem(ITileWalkabilityGrid? walkabilityGrid, float tileSize = 32f)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tileSize);
        _walkabilityGrid = walkabilityGrid;
        _tileSize = tileSize;
    }

    /// <inheritdoc/>
    public SimulationPhase Phase => SimulationPhase.PreUpdate;

    /// <inheritdoc/>
    public void Update(WorldSimulation world, FixedTickArgs tick)
    {
        ArgumentNullException.ThrowIfNull(world);

        EntityManager entities = world.Entities;
        ComponentPool<AiBrainComponent> pool = entities.GetPool<AiBrainComponent>();
        ReadOnlySpan<int> slots = pool.EntitySlots;
        Span<AiBrainComponent> brains = pool.AsSpan();
        IWorldPhysics2D? physics = world.WorldPhysics ?? world.ObstacleGrid;
        EntityAttachmentStore<ThreatTable> threatStore = entities.GetAttachmentStore<ThreatTable>();
        EntityAttachmentStore<PathWaypointBuffer> pathStore = entities.GetAttachmentStore<PathWaypointBuffer>();
        Span<Entity> aggroScratch = stackalloc Entity[MaxAggroCandidates];
        Span<Entity> neighborScratch = stackalloc Entity[MaxSeparationNeighbors];

        for (int i = 0; i < slots.Length; i++)
        {
            Entity aiEntity = entities.GetEntity(slots[i]);
            if (!aiEntity.IsValid || !entities.HasTransform(aiEntity))
            {
                continue;
            }

            ref AiBrainComponent brain = ref brains[i];
            if (brain.RepathCooldownTicks > 0)
            {
                brain = brain with { RepathCooldownTicks = brain.RepathCooldownTicks - 1 };
            }

            Vector2 pos = entities.GetTransform(aiEntity).Position;
            float entityRadius = entities.HasCollider(aiEntity) ? entities.GetCollider(aiEntity).Radius : 7f;

            switch (brain.State)
            {
                case AiState.Idle:
                case AiState.Patrol:
                    Entity target = SelectTarget(world, entities, threatStore, aiEntity, pos, in brain, aggroScratch);
                    if (target.IsValid)
                    {
                        brain = brain with
                        {
                            State = AiState.Chase,
                            TargetEntity = target,
                            RepathCooldownTicks = 0
                        };
                    }
                    else if (brain.State == AiState.Patrol && brain.PatrolRadius > 0f && brain.MoveSpeed > 0f)
                    {
                        brain = this.StepPatrol(world, aiEntity, pos, entityRadius, in brain, tick.TickIndex, physics);
                    }
                    else
                    {
                        SetEntityVelocity(world, aiEntity, Vector2.Zero);
                    }

                    break;

                case AiState.Chase:
                    Entity highestThreat = SelectHighestThreatIfAvailable(entities, threatStore, aiEntity);
                    if (highestThreat.IsValid && highestThreat != brain.TargetEntity)
                    {
                        brain = brain with { TargetEntity = highestThreat, RepathCooldownTicks = 0 };
                    }

                    if (!IsValidLivingTarget(entities, brain.TargetEntity) ||
                        Vector2.DistanceSquared(pos, brain.HomePosition) > (brain.LeashRadius * brain.LeashRadius))
                    {
                        ResetAggroAndPath(threatStore, pathStore, aiEntity);
                        brain = brain with
                        {
                            State = AiState.ReturnHome,
                            TargetEntity = Entity.Null,
                            WindupRemainingTicks = 0,
                            RepathCooldownTicks = 0
                        };
                        break;
                    }

                    Vector2 targetPos = entities.GetTransform(brain.TargetEntity).Position;
                    Vector2 toTarget = targetPos - pos;
                    float distSq = toTarget.LengthSquared();
                    bool hasClearance = HasNavigationClearance(world, pos, targetPos, entityRadius);

                    if (hasClearance && distSq <= brain.AttackRange * brain.AttackRange)
                    {
                        SetEntityVelocity(world, aiEntity, Vector2.Zero);
                        if (pathStore.TryGet(aiEntity, out PathWaypointBuffer? chasePath))
                        {
                            chasePath.Clear();
                        }

                        brain = brain with { State = AiState.Attack, WindupRemainingTicks = 0 };
                    }
                    else
                    {
                        float speedMult = physics is not null ? physics.GetMoveSpeedMultiplier(pos) : 1f;
                        Vector2 moveDir = this.ComputeChaseDirection(
                            world,
                            aiEntity,
                            pos,
                            targetPos,
                            hasClearance,
                            pathStore,
                            ref brain,
                            distSq,
                            toTarget,
                            neighborScratch);
                        SetEntityVelocity(world, aiEntity, moveDir * (brain.MoveSpeed * speedMult));
                    }

                    break;

                case AiState.Attack:
                    Entity activeThreat = SelectHighestThreatIfAvailable(entities, threatStore, aiEntity);
                    if (activeThreat.IsValid && activeThreat != brain.TargetEntity)
                    {
                        brain = brain with
                        {
                            TargetEntity = activeThreat,
                            State = AiState.Chase,
                            WindupRemainingTicks = 0,
                            RepathCooldownTicks = 0
                        };
                        break;
                    }

                    if (!IsValidLivingTarget(entities, brain.TargetEntity) ||
                        Vector2.DistanceSquared(pos, brain.HomePosition) > (brain.LeashRadius * brain.LeashRadius))
                    {
                        ResetAggroAndPath(threatStore, pathStore, aiEntity);
                        SetEntityVelocity(world, aiEntity, Vector2.Zero);
                        brain = brain with
                        {
                            State = AiState.ReturnHome,
                            TargetEntity = Entity.Null,
                            WindupRemainingTicks = 0,
                            RepathCooldownTicks = 0
                        };
                        break;
                    }

                    Vector2 atkTargetPos = entities.GetTransform(brain.TargetEntity).Position;
                    float disengageRange = brain.AttackRange * 1.18f;
                    bool inDisengageRange = Vector2.DistanceSquared(pos, atkTargetPos) <= (disengageRange * disengageRange);
                    bool canSeeTarget = HasLineOfSight(world, pos, atkTargetPos);

                    if (brain.WindupRemainingTicks == 0 && (!canSeeTarget || !inDisengageRange))
                    {
                        brain = brain with { State = AiState.Chase, RepathCooldownTicks = 0 };
                        break;
                    }

                    if (brain.WindupTicks == 0)
                    {
                        SetEntityVelocity(world, aiEntity, Vector2.Zero);
                        if (brain.CooldownRemainingTicks == 0)
                        {
                            world.PublishEvent(new AiAttackReadyEvent(aiEntity, brain.TargetEntity));
                            brain = brain with { CooldownRemainingTicks = brain.AttackCooldownTicks };
                        }
                        else
                        {
                            brain = brain with { CooldownRemainingTicks = brain.CooldownRemainingTicks - 1 };
                        }
                    }
                    else if (brain.WindupRemainingTicks > 0)
                    {
                        uint nextWindup = brain.WindupRemainingTicks - 1;
                        if (nextWindup <= 2 && nextWindup > 0)
                        {
                            Vector2 lungeDelta = atkTargetPos - pos;
                            float lungeLenSq = lungeDelta.LengthSquared();
                            Vector2 lungeVel = lungeLenSq > 1e-4f
                                ? lungeDelta / MathF.Sqrt(lungeLenSq) * (brain.MoveSpeed * 1.4f)
                                : Vector2.Zero;
                            SetEntityVelocity(world, aiEntity, lungeVel);
                            brain = brain with { WindupRemainingTicks = nextWindup };
                        }
                        else if (nextWindup == 0)
                        {
                            SetEntityVelocity(world, aiEntity, Vector2.Zero);
                            if (inDisengageRange && canSeeTarget)
                            {
                                world.PublishEvent(new AiAttackReadyEvent(aiEntity, brain.TargetEntity));
                                brain = brain with
                                {
                                    WindupRemainingTicks = 0,
                                    CooldownRemainingTicks = brain.AttackCooldownTicks
                                };
                            }
                            else
                            {
                                brain = brain with
                                {
                                    State = AiState.Chase,
                                    WindupRemainingTicks = 0,
                                    CooldownRemainingTicks = Math.Max(4U, brain.AttackCooldownTicks / 2U),
                                    RepathCooldownTicks = 0
                                };
                            }
                        }
                        else
                        {
                            SetEntityVelocity(world, aiEntity, Vector2.Zero);
                            brain = brain with { WindupRemainingTicks = nextWindup };
                        }
                    }
                    else if (brain.CooldownRemainingTicks == 0)
                    {
                        SetEntityVelocity(world, aiEntity, Vector2.Zero);
                        brain = brain with { WindupRemainingTicks = brain.WindupTicks };
                    }
                    else
                    {
                        SetEntityVelocity(world, aiEntity, Vector2.Zero);
                        brain = brain with { CooldownRemainingTicks = brain.CooldownRemainingTicks - 1 };
                    }

                    break;

                case AiState.ReturnHome:
                    Vector2 toHome = brain.HomePosition - pos;
                    float homeDistSq = toHome.LengthSquared();
                    if (homeDistSq <= 16f)
                    {
                        SetEntityVelocity(world, aiEntity, Vector2.Zero);
                        if (pathStore.TryGet(aiEntity, out PathWaypointBuffer? returnPath))
                        {
                            returnPath.Clear();
                        }

                        if (entities.HasHealth(aiEntity))
                        {
                            ref HealthComponent hp = ref entities.GetHealth(aiEntity);
                            if (hp.Current < hp.Max)
                            {
                                hp = new HealthComponent(hp.Max, hp.Max);
                                world.MarkDirty(aiEntity, EntityDirtyMask.Health);
                            }
                        }

                        brain = brain with
                        {
                            State = brain.PatrolRadius > 0f ? AiState.Patrol : AiState.Idle,
                            TargetEntity = Entity.Null,
                            CooldownRemainingTicks = 0,
                            WindupRemainingTicks = 0,
                            RepathCooldownTicks = 0
                        };
                    }
                    else
                    {
                        float speedMult = physics is not null ? physics.GetMoveSpeedMultiplier(pos) : 1f;
                        Vector2 dirHome = this.ComputeReturnDirection(
                            world,
                            aiEntity,
                            entityRadius,
                            pos,
                            brain.HomePosition,
                            pathStore,
                            ref brain,
                            homeDistSq,
                            toHome);
                        SetEntityVelocity(world, aiEntity, dirHome * (brain.MoveSpeed * speedMult));
                    }

                    break;

                default:
                    break;
            }
        }
    }

    private Vector2 ComputeChaseDirection(
        WorldSimulation world,
        Entity aiEntity,
        Vector2 pos,
        Vector2 targetPos,
        bool hasClearance,
        EntityAttachmentStore<PathWaypointBuffer> pathStore,
        ref AiBrainComponent brain,
        float directDistSq,
        Vector2 directDelta,
        Span<Entity> neighborScratch)
    {
        Vector2 desiredDir;
        if (!hasClearance && _walkabilityGrid is not null)
        {
            PathWaypointBuffer buffer = pathStore.GetOrAdd(aiEntity, static () => new PathWaypointBuffer());
            if (!buffer.HasPath || brain.RepathCooldownTicks == 0)
            {
                _ = buffer.TryComputePath(_pathfinder, _walkabilityGrid, pos, targetPos, _tileSize);
                brain = brain with { RepathCooldownTicks = ChaseRepathIntervalTicks };
            }

            if (buffer.TryGetSteeringDirection(pos, _tileSize, _tileSize * 0.45f, out Vector2 pathDir))
            {
                desiredDir = pathDir;
            }
            else
            {
                desiredDir = directDistSq > 1e-6f ? (directDelta / MathF.Sqrt(directDistSq)) : Vector2.Zero;
            }
        }
        else
        {
            if (pathStore.TryGet(aiEntity, out PathWaypointBuffer? existingPath) && existingPath.HasPath)
            {
                existingPath.Clear();
            }

            desiredDir = directDistSq > 1e-6f ? (directDelta / MathF.Sqrt(directDistSq)) : Vector2.Zero;
            float flankThreshold = brain.AttackRange * 1.6f;
            if (hasClearance && directDistSq > flankThreshold * flankThreshold && desiredDir != Vector2.Zero)
            {
                float flankSign = (aiEntity.Id & 1) == 0 ? 1f : -1f;
                Vector2 perp = new(-desiredDir.Y, desiredDir.X);
                desiredDir += perp * (flankSign * 0.20f);
            }
        }

        if (desiredDir == Vector2.Zero)
        {
            return Vector2.Zero;
        }

        Vector2 separation = ComputeCrowdSeparation(world, aiEntity, pos, neighborScratch);
        Vector2 combined = desiredDir + separation;
        float combinedLenSq = combined.LengthSquared();
        return combinedLenSq > 1e-6f ? combined / MathF.Sqrt(combinedLenSq) : desiredDir;
    }

    private Vector2 ComputeReturnDirection(
        WorldSimulation world,
        Entity aiEntity,
        float entityRadius,
        Vector2 pos,
        Vector2 homePos,
        EntityAttachmentStore<PathWaypointBuffer> pathStore,
        ref AiBrainComponent brain,
        float homeDistSq,
        Vector2 toHome)
    {
        if (!HasNavigationClearance(world, pos, homePos, entityRadius) && _walkabilityGrid is not null)
        {
            PathWaypointBuffer buffer = pathStore.GetOrAdd(aiEntity, static () => new PathWaypointBuffer());
            if (!buffer.HasPath || brain.RepathCooldownTicks == 0)
            {
                _ = buffer.TryComputePath(_pathfinder, _walkabilityGrid, pos, homePos, _tileSize);
                brain = brain with { RepathCooldownTicks = ReturnRepathIntervalTicks };
            }

            if (buffer.TryGetSteeringDirection(pos, _tileSize, _tileSize * 0.45f, out Vector2 pathDir))
            {
                return pathDir;
            }
        }
        else if (pathStore.TryGet(aiEntity, out PathWaypointBuffer? existingPath) && existingPath.HasPath)
        {
            existingPath.Clear();
        }

        return homeDistSq > 1e-6f ? (toHome / MathF.Sqrt(homeDistSq)) : Vector2.Zero;
    }

    private static Vector2 ComputeCrowdSeparation(
        WorldSimulation world,
        Entity self,
        Vector2 pos,
        Span<Entity> scratch)
    {
        const float SeparationRadius = 26f;
        world.OverlapCircle(pos, SeparationRadius, scratch, out int neighborCount);
        Vector2 push = Vector2.Zero;

        for (int i = 0; i < neighborCount; i++)
        {
            Entity other = scratch[i];
            if (other == self ||
                !world.Entities.IsAlive(other) ||
                !world.Entities.HasTransform(other) ||
                !world.Entities.HasComponent<AiBrainComponent>(other))
            {
                continue;
            }

            Vector2 away = pos - world.Entities.GetTransform(other).Position;
            float distSq = away.LengthSquared();
            if (distSq > 1e-4f && distSq < SeparationRadius * SeparationRadius)
            {
                float dist = MathF.Sqrt(distSq);
                float strength = (SeparationRadius - dist) / SeparationRadius;
                push += away / dist * (strength * 0.65f);
            }
        }

        return push;
    }

    private AiBrainComponent StepPatrol(
        WorldSimulation world,
        Entity aiEntity,
        Vector2 pos,
        float entityRadius,
        in AiBrainComponent brain,
        ulong tickIndex,
        IWorldPhysics2D? physics)
    {
        if (brain.PatrolWaitTicks > 0)
        {
            SetEntityVelocity(world, aiEntity, Vector2.Zero);
            return brain with { PatrolWaitTicks = brain.PatrolWaitTicks - 1 };
        }

        Vector2 destination = brain.PatrolDestination;
        Vector2 toDest = destination - pos;
        if (destination == default || toDest.LengthSquared() <= 16f || !HasNavigationClearance(world, pos, destination, entityRadius))
        {
            // Pick a deterministic walkable patrol waypoint inside PatrolRadius around HomePosition.
            Vector2 chosen = brain.HomePosition;
            uint baseHash = (uint)(tickIndex * 2654435761UL) ^ (uint)(aiEntity.Id * 73856093);

            for (int attempt = 0; attempt < 6; attempt++)
            {
                uint hash = baseHash ^ (uint)(attempt * 0x9E3779B9U);
                float angle = (hash & 0xFFFF) * (6.2831853f / 65535f);
                float radialFraction = 0.35f + (0.65f * (((hash >> 16) & 0xFFFF) / 65535f));
                Vector2 candidate = brain.HomePosition + (new Vector2(DeterministicMath.Cos(angle), DeterministicMath.Sin(angle)) * (brain.PatrolRadius * radialFraction));

                int tx = (int)MathF.Floor(candidate.X / _tileSize);
                int ty = (int)MathF.Floor(candidate.Y / _tileSize);
                if (_walkabilityGrid is not null && !_walkabilityGrid.IsWalkable(tx, ty))
                {
                    continue;
                }

                if (HasNavigationClearance(world, pos, candidate, entityRadius))
                {
                    chosen = candidate;
                    break;
                }
            }

            SetEntityVelocity(world, aiEntity, Vector2.Zero);
            return brain with
            {
                PatrolDestination = chosen,
                PatrolWaitTicks = PatrolDefaultWaitTicks
            };
        }

        float dist = MathF.Sqrt(toDest.LengthSquared());
        float speedMult = physics is not null ? physics.GetMoveSpeedMultiplier(pos) : 1f;
        SetEntityVelocity(world, aiEntity, toDest * (brain.MoveSpeed * 0.6f * speedMult / dist));
        return brain;
    }

    private static Entity SelectTarget(
        WorldSimulation world,
        EntityManager entities,
        EntityAttachmentStore<ThreatTable> threatStore,
        Entity aiEntity,
        Vector2 origin,
        in AiBrainComponent brain,
        Span<Entity> scratch)
    {
        Entity threatTarget = SelectHighestThreatIfAvailable(entities, threatStore, aiEntity);
        if (threatTarget.IsValid)
        {
            return threatTarget;
        }

        return FindClosestTarget(world, aiEntity, origin, brain.AggroRadius, brain.TargetLayerMask, scratch);
    }

    private static Entity SelectHighestThreatIfAvailable(
        EntityManager entities,
        EntityAttachmentStore<ThreatTable> threatStore,
        Entity aiEntity)
    {
        if (threatStore.TryGet(aiEntity, out ThreatTable? threats))
        {
            return threats.SelectHighestThreatTarget(entities);
        }

        return Entity.Null;
    }

    private static void ResetAggroAndPath(
        EntityAttachmentStore<ThreatTable> threatStore,
        EntityAttachmentStore<PathWaypointBuffer> pathStore,
        Entity aiEntity)
    {
        if (threatStore.TryGet(aiEntity, out ThreatTable? threats))
        {
            threats.Clear();
        }

        if (pathStore.TryGet(aiEntity, out PathWaypointBuffer? path))
        {
            path.Clear();
        }
    }

    private static Entity FindClosestTarget(
        WorldSimulation world,
        Entity self,
        Vector2 origin,
        float aggroRadius,
        uint targetLayerMask,
        Span<Entity> scratch)
    {
        if (aggroRadius <= 0f)
        {
            return Entity.Null;
        }

        world.OverlapCircle(origin, aggroRadius, scratch, out int count, targetLayerMask);
        Entity best = Entity.Null;
        float bestDistSq = float.PositiveInfinity;

        for (int i = 0; i < count; i++)
        {
            Entity candidate = scratch[i];
            if (candidate == self || !IsValidLivingTarget(world.Entities, candidate))
            {
                continue;
            }

            Vector2 candidatePos = world.Entities.GetTransform(candidate).Position;
            float dSq = Vector2.DistanceSquared(origin, candidatePos);
            if (dSq < bestDistSq && HasLineOfSight(world, origin, candidatePos))
            {
                bestDistSq = dSq;
                best = candidate;
            }
        }

        return best;
    }

    private static bool HasLineOfSight(WorldSimulation world, Vector2 from, Vector2 to)
    {
        Vector2 delta = to - from;
        float dist = delta.Length();
        if (dist <= 1e-3f)
        {
            return true;
        }

        Ray2D ray = new(from, delta / dist, dist);
        return !world.Raycast(in ray, out _);
    }

    private static bool HasNavigationClearance(WorldSimulation world, Vector2 from, Vector2 to, float entityRadius)
    {
        Vector2 delta = to - from;
        float dist = delta.Length();
        if (dist <= 1e-3f)
        {
            return true;
        }

        Vector2 dir = delta / dist;
        Ray2D center = new(from, dir, dist);
        if (world.Raycast(in center, out _))
        {
            return false;
        }

        float shoulder = MathF.Min(MathF.Max(entityRadius * 0.85f, 3f), 9f);
        Vector2 perp = new(-dir.Y * shoulder, dir.X * shoulder);
        Ray2D left = new(from + perp, dir, dist);
        if (world.Raycast(in left, out _))
        {
            return false;
        }

        Ray2D right = new(from - perp, dir, dist);
        return !world.Raycast(in right, out _);
    }

    private static bool IsValidLivingTarget(EntityManager entities, Entity target) =>
        target.IsValid &&
        entities.IsAlive(target) &&
        entities.HasTransform(target) &&
        (!entities.HasHealth(target) || !entities.GetHealth(target).IsDead);

    private static void SetEntityVelocity(WorldSimulation world, Entity entity, Vector2 linear)
    {
        if (!world.Entities.HasVelocity(entity))
        {
            return;
        }

        ref Velocity2D vel = ref world.Entities.GetVelocity(entity);
        if (vel.Linear != linear)
        {
            vel.Linear = linear;
            world.MarkDirty(entity, EntityDirtyMask.Velocity);
        }
    }
}
