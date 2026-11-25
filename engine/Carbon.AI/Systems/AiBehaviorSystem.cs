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
    private const uint PatrolDefaultWaitTicks = 20;

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

        for (int i = 0; i < slots.Length; i++)
        {
            Entity aiEntity = entities.GetEntity(slots[i]);
            if (!aiEntity.IsValid || !entities.HasTransform(aiEntity))
            {
                continue;
            }

            ref AiBrainComponent brain = ref brains[i];
            Vector2 pos = entities.GetTransform(aiEntity).Position;

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
                            TargetEntity = target
                        };
                    }
                    else if (brain.State == AiState.Patrol && brain.PatrolRadius > 0f && brain.MoveSpeed > 0f)
                    {
                        brain = this.StepPatrol(world, aiEntity, pos, in brain, tick.TickIndex, physics);
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
                        brain = brain with { TargetEntity = highestThreat };
                    }

                    if (!IsValidLivingTarget(entities, brain.TargetEntity) ||
                        Vector2.DistanceSquared(pos, brain.HomePosition) > (brain.LeashRadius * brain.LeashRadius))
                    {
                        ResetAggroAndPath(threatStore, pathStore, aiEntity);
                        brain = brain with
                        {
                            State = AiState.ReturnHome,
                            TargetEntity = Entity.Null
                        };
                        break;
                    }

                    Vector2 targetPos = entities.GetTransform(brain.TargetEntity).Position;
                    Vector2 toTarget = targetPos - pos;
                    float distSq = toTarget.LengthSquared();
                    bool hasLos = HasLineOfSight(world, pos, targetPos);

                    if (hasLos && distSq <= brain.AttackRange * brain.AttackRange)
                    {
                        SetEntityVelocity(world, aiEntity, Vector2.Zero);
                        if (pathStore.TryGet(aiEntity, out PathWaypointBuffer? chasePath))
                        {
                            chasePath.Clear();
                        }

                        brain = brain with { State = AiState.Attack };
                    }
                    else
                    {
                        float speedMult = physics is not null ? physics.GetMoveSpeedMultiplier(pos) : 1f;
                        Vector2 moveDir = this.ComputeChaseDirection(pos, targetPos, hasLos, pathStore, aiEntity, distSq, toTarget);
                        SetEntityVelocity(world, aiEntity, moveDir * (brain.MoveSpeed * speedMult));
                    }

                    break;

                case AiState.Attack:
                    Entity activeThreat = SelectHighestThreatIfAvailable(entities, threatStore, aiEntity);
                    if (activeThreat.IsValid && activeThreat != brain.TargetEntity)
                    {
                        brain = brain with { TargetEntity = activeThreat, State = AiState.Chase };
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
                            TargetEntity = Entity.Null
                        };
                        break;
                    }

                    Vector2 atkTargetPos = entities.GetTransform(brain.TargetEntity).Position;
                    float disengageRange = brain.AttackRange * 1.15f;
                    if (!HasLineOfSight(world, pos, atkTargetPos) ||
                        Vector2.DistanceSquared(pos, atkTargetPos) > (disengageRange * disengageRange))
                    {
                        brain = brain with { State = AiState.Chase };
                        break;
                    }

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
                            CooldownRemainingTicks = 0
                        };
                    }
                    else
                    {
                        Vector2 dirHome = toHome / MathF.Sqrt(homeDistSq);
                        SetEntityVelocity(world, aiEntity, dirHome * brain.MoveSpeed);
                    }

                    break;

                default:
                    break;
            }
        }
    }

    private Vector2 ComputeChaseDirection(
        Vector2 pos,
        Vector2 targetPos,
        bool hasLos,
        EntityAttachmentStore<PathWaypointBuffer> pathStore,
        Entity aiEntity,
        float directDistSq,
        Vector2 directDelta)
    {
        if (!hasLos && _walkabilityGrid is not null)
        {
            PathWaypointBuffer buffer = pathStore.GetOrAdd(aiEntity, static () => new PathWaypointBuffer());
            if (!buffer.HasPath ||
                buffer.TryComputePath(_pathfinder, _walkabilityGrid, pos, targetPos, _tileSize) == PathSearchStatus.Found)
            {
                if (buffer.TryGetSteeringDirection(pos, _tileSize, _tileSize * 0.45f, out Vector2 pathDir))
                {
                    return pathDir;
                }
            }
        }

        return directDistSq > 1e-6f ? (directDelta / MathF.Sqrt(directDistSq)) : Vector2.Zero;
    }

    private AiBrainComponent StepPatrol(
        WorldSimulation world,
        Entity aiEntity,
        Vector2 pos,
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
        if (destination == default || toDest.LengthSquared() <= 16f)
        {
            // Pick a deterministic patrol waypoint inside PatrolRadius around HomePosition.
            uint hash = (uint)(tickIndex * 2654435761UL) ^ (uint)(aiEntity.Id * 73856093);
            float angle = (hash & 0xFFFF) * (6.2831853f / 65535f);
            float radialFraction = 0.35f + (0.65f * (((hash >> 16) & 0xFFFF) / 65535f));
            Vector2 nextDest = brain.HomePosition + (new Vector2(DeterministicMath.Cos(angle), DeterministicMath.Sin(angle)) * (brain.PatrolRadius * radialFraction));
            SetEntityVelocity(world, aiEntity, Vector2.Zero);
            return brain with
            {
                PatrolDestination = nextDest,
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
