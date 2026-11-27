// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.AI.Components;
using Carbon.AI.Navigation;
using Carbon.AI.Tactics;
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
/// <c>Idle / Patrol -> Chase -> Attack -> ReturnHome (Leash)</c> plus <c>Flee</c>. Integrates <see cref="ThreatTable"/> aggro
/// with switch hysteresis and taunts, line-of-sight <see cref="WorldSimulation.Raycast"/> checks, and budgeted
/// <see cref="PathWaypointBuffer"/> navigation (at most <see cref="MaxPathSearchesPerTick"/> A* searches per tick).
/// Optional <see cref="AiTacticsComponent"/> data adds pack assist, fleeing, give-up timers, surround slots, and night
/// aggression. Executes during <see cref="SimulationPhase.PreUpdate"/>. Zero-allocation in steady state.
/// </summary>
public sealed class AiBehaviorSystem : ISimulationSystem
{
    private const int MaxAggroCandidates = 64;
    private const int MaxSeparationNeighbors = 16;
    private const uint PatrolDefaultWaitTicks = 20;
    private const uint ChaseRepathIntervalTicks = 10;
    private const uint ReturnRepathIntervalTicks = 15;
    private const float FleeProbeDistance = 28f;
    private const float SurroundEngageRingFactor = 6f;

    private static readonly AiTacticsComponent s_noTactics = new(PackId: 0);

    private readonly GridPathfinder _pathfinder = new();
    private readonly ITileWalkabilityGrid? _walkabilityGrid;

    // The entity being updated: its collision mask and the grid as it sees it (a water creature paths only through
    // its own depth tiers, ADR-032 D14). Set at the top of each entity's update; the system is single-threaded.
    private uint _terrainMask = uint.MaxValue;
    private ITileWalkabilityGrid? _grid;
    private readonly float _tileSize;
    private int _pathSearchesThisTick;
    private int _pathSearchesDeferredThisTick;

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

    /// <summary>
    /// Gets or sets the CPU budget: the maximum number of A* searches run per tick across all actors. Actors over budget
    /// keep their previous path (or steer directly) and retry on the next tick, so searches are time-sliced.
    /// </summary>
    public int MaxPathSearchesPerTick { get; set; } = 8;

    /// <summary>Gets or sets the node expansion cap of a single A* search.</summary>
    public int MaxPathNodesPerSearch { get; set; } = 768;

    /// <summary>Gets the number of A* searches run during the last tick.</summary>
    public int PathSearchesLastTick { get; private set; }

    /// <summary>Gets the number of A* searches postponed by the per-tick budget during the last tick.</summary>
    public int PathSearchesDeferredLastTick { get; private set; }

    /// <summary>
    /// Gets or sets a value indicating whether it is night. The host sets it from its authoritative clock; at night
    /// <see cref="AiTacticsComponent.NightAggroMultiplier"/> and <see cref="AiTacticsComponent.NightLeashMultiplier"/> apply.
    /// </summary>
    public bool IsNight { get; set; }

    /// <inheritdoc/>
    public void Update(WorldSimulation world, FixedTickArgs tick)
    {
        ArgumentNullException.ThrowIfNull(world);

        _pathSearchesThisTick = 0;
        _pathSearchesDeferredThisTick = 0;

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
            _terrainMask = entities.HasCollider(aiEntity) ? entities.GetCollider(aiEntity).Mask : uint.MaxValue;
            _grid = _walkabilityGrid?.ForTerrainMask(_terrainMask);
            AiTacticsComponent tactics = entities.TryGetComponent(aiEntity, out AiTacticsComponent t) ? t : s_noTactics;
            float aggroMult = this.IsNight && tactics.NightAggroMultiplier > 0f ? tactics.NightAggroMultiplier : 1f;
            float leashMult = this.IsNight && tactics.NightLeashMultiplier > 0f ? tactics.NightLeashMultiplier : 1f;
            float leashRadius = brain.LeashRadius * leashMult;
            float leashSq = leashRadius * leashRadius;

            switch (brain.State)
            {
                case AiState.Idle:
                case AiState.Patrol:
                    Entity target = SelectTarget(world, entities, threatStore, aiEntity, pos, in brain, in tactics, aggroMult, tick.TickIndex, aggroScratch);
                    if (target.IsValid)
                    {
                        brain = brain with
                        {
                            State = AiState.Chase,
                            TargetEntity = target,
                            RepathCooldownTicks = 0,
                            ChaseTicks = 0
                        };

                        // Social aggro: the pack joins the pull (alerted allies do not re-broadcast).
                        _ = AiPack.CallForHelp(world, aiEntity, pos, target, 1f, aggroMult, aggroScratch);
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
                    Entity selected = SelectThreatTarget(entities, threatStore, aiEntity, brain.TargetEntity, in tactics, tick.TickIndex);
                    if (selected.IsValid && selected != brain.TargetEntity)
                    {
                        brain = brain with { TargetEntity = selected, RepathCooldownTicks = 0 };
                    }

                    if (!IsValidLivingTarget(entities, brain.TargetEntity) ||
                        Vector2.DistanceSquared(pos, brain.HomePosition) > leashSq ||
                        (tactics.GiveUpTicks > 0 && brain.ChaseTicks >= tactics.GiveUpTicks))
                    {
                        BeginReturnHome(world, aiEntity, threatStore, pathStore, ref brain);
                        break;
                    }

                    if (this.TryBeginFlee(world, aiEntity, pos, in tactics, aggroMult, aggroScratch, ref brain))
                    {
                        break;
                    }

                    Vector2 targetPos = entities.GetTransform(brain.TargetEntity).Position;
                    Vector2 toTarget = targetPos - pos;
                    float distSq = toTarget.LengthSquared();
                    bool hasClearance = this.HasNavigationClearance(world, pos, targetPos, entityRadius);

                    if (hasClearance && distSq <= brain.AttackRange * brain.AttackRange)
                    {
                        SetEntityVelocity(world, aiEntity, Vector2.Zero);
                        if (pathStore.TryGet(aiEntity, out PathWaypointBuffer? chasePath))
                        {
                            chasePath.Clear();
                        }

                        brain = brain with { State = AiState.Attack, WindupRemainingTicks = 0, ChaseTicks = 0 };
                    }
                    else
                    {
                        brain = brain with { ChaseTicks = brain.ChaseTicks + 1 };
                        float speedMult = physics is not null ? physics.GetMoveSpeedMultiplier(pos, _terrainMask) : 1f;
                        Vector2 moveDir = this.ComputeChaseDirection(
                            world,
                            aiEntity,
                            pos,
                            targetPos,
                            hasClearance,
                            pathStore,
                            ref brain,
                            in tactics,
                            distSq,
                            toTarget,
                            neighborScratch);
                        SetEntityVelocity(world, aiEntity, moveDir * (brain.MoveSpeed * speedMult));
                    }

                    break;

                case AiState.Attack:
                    Entity activeThreat = SelectThreatTarget(entities, threatStore, aiEntity, brain.TargetEntity, in tactics, tick.TickIndex);
                    if (activeThreat.IsValid && activeThreat != brain.TargetEntity)
                    {
                        brain = brain with
                        {
                            TargetEntity = activeThreat,
                            State = AiState.Chase,
                            WindupRemainingTicks = 0,
                            RepathCooldownTicks = 0,
                            ChaseTicks = 0
                        };
                        break;
                    }

                    if (!IsValidLivingTarget(entities, brain.TargetEntity) ||
                        Vector2.DistanceSquared(pos, brain.HomePosition) > leashSq)
                    {
                        BeginReturnHome(world, aiEntity, threatStore, pathStore, ref brain);
                        break;
                    }

                    if (this.TryBeginFlee(world, aiEntity, pos, in tactics, aggroMult, aggroScratch, ref brain))
                    {
                        break;
                    }

                    this.StepAttack(world, aiEntity, pos, entityRadius, ref brain);
                    break;

                case AiState.Flee:
                    if (Vector2.DistanceSquared(pos, brain.HomePosition) > leashSq || !IsValidLivingTarget(entities, brain.TargetEntity))
                    {
                        BeginReturnHome(world, aiEntity, threatStore, pathStore, ref brain);
                        break;
                    }

                    if (brain.FleeRemainingTicks == 0)
                    {
                        brain = brain with { State = AiState.Chase, RepathCooldownTicks = 0, ChaseTicks = 0 };
                        break;
                    }

                    Vector2 away = pos - entities.GetTransform(brain.TargetEntity).Position;
                    float awayLenSq = away.LengthSquared();
                    Vector2 fleeDir = awayLenSq > 1e-6f ? away / MathF.Sqrt(awayLenSq) : Vector2.UnitX;
                    float side = (aiEntity.Id & 1) == 0 ? 1f : -1f;
                    Vector2 fleeVelocity = Vector2.Zero;
                    if (AiSteering.TryFindOpenDirection(world, pos, fleeDir, FleeProbeDistance, side, out Vector2 openDir))
                    {
                        float fleeSpeedMult = physics is not null ? physics.GetMoveSpeedMultiplier(pos, _terrainMask) : 1f;
                        fleeVelocity = openDir * (brain.MoveSpeed * fleeSpeedMult);
                    }

                    SetEntityVelocity(world, aiEntity, fleeVelocity);
                    brain = brain with { FleeRemainingTicks = brain.FleeRemainingTicks - 1 };
                    break;

                case AiState.ReturnHome:
                    Vector2 toHome = brain.HomePosition - pos;
                    float homeDistSq = toHome.LengthSquared();
                    if (homeDistSq <= 64f)
                    {
                        SetEntityVelocity(world, aiEntity, Vector2.Zero);
                        ResetAggroAndPath(threatStore, pathStore, aiEntity);

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
                            RepathCooldownTicks = 0,
                            ChaseTicks = 0,
                            FleeRemainingTicks = 0,
                            HasFled = false
                        };
                    }
                    else
                    {
                        if (entities.HasHealth(aiEntity))
                        {
                            ref HealthComponent hp = ref entities.GetHealth(aiEntity);
                            if (hp.Current < hp.Max)
                            {
                                float healed = MathF.Min(hp.Max, hp.Current + (hp.Max * 0.25f * tick.DeltaTime));
                                hp = new HealthComponent(healed, hp.Max);
                                world.MarkDirty(aiEntity, EntityDirtyMask.Health);
                            }
                        }

                        float speedMult = physics is not null ? physics.GetMoveSpeedMultiplier(pos, _terrainMask) : 1f;
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

        this.PathSearchesLastTick = _pathSearchesThisTick;
        this.PathSearchesDeferredLastTick = _pathSearchesDeferredThisTick;
    }

    private static void BeginReturnHome(
        WorldSimulation world,
        Entity aiEntity,
        EntityAttachmentStore<ThreatTable> threatStore,
        EntityAttachmentStore<PathWaypointBuffer> pathStore,
        ref AiBrainComponent brain)
    {
        ResetAggroAndPath(threatStore, pathStore, aiEntity);
        if (brain.State != AiState.Chase)
        {
            SetEntityVelocity(world, aiEntity, Vector2.Zero);
        }

        brain = brain with
        {
            State = AiState.ReturnHome,
            TargetEntity = Entity.Null,
            WindupRemainingTicks = 0,
            RepathCooldownTicks = 0,
            ChaseTicks = 0,
            FleeRemainingTicks = 0
        };
    }

    private bool TryBeginFlee(
        WorldSimulation world,
        Entity aiEntity,
        Vector2 pos,
        in AiTacticsComponent tactics,
        float assistMult,
        Span<Entity> scratch,
        ref AiBrainComponent brain)
    {
        if (brain.HasFled || tactics.FleeHealthFraction <= 0f || tactics.FleeTicks == 0 || !world.Entities.HasHealth(aiEntity))
        {
            return false;
        }

        HealthComponent hp = world.Entities.GetHealth(aiEntity);
        if (hp.IsDead || hp.Max <= 0f || hp.Current > hp.Max * tactics.FleeHealthFraction)
        {
            return false;
        }

        brain = brain with
        {
            State = AiState.Flee,
            FleeRemainingTicks = tactics.FleeTicks,
            HasFled = true,
            WindupRemainingTicks = 0,
            ChaseTicks = 0
        };

        // A wounded pack member screams for help; allies weigh its attacker heavily.
        _ = AiPack.CallForHelp(world, aiEntity, pos, brain.TargetEntity, hp.Max * 0.5f, assistMult, scratch);
        return true;
    }

    private void StepAttack(WorldSimulation world, Entity aiEntity, Vector2 pos, float entityRadius, ref AiBrainComponent brain)
    {
        EntityManager entities = world.Entities;
        Vector2 atkTargetPos = entities.GetTransform(brain.TargetEntity).Position;
        float disengageRange = brain.AttackRange * 1.18f;
        bool inDisengageRange = Vector2.DistanceSquared(pos, atkTargetPos) <= (disengageRange * disengageRange);
        bool canSeeTarget = HasLineOfSight(world, pos, atkTargetPos);

        if (brain.WindupRemainingTicks == 0 && (!canSeeTarget || !inDisengageRange))
        {
            brain = brain with { State = AiState.Chase, RepathCooldownTicks = 0 };
            return;
        }

        if (brain.ExternalAttackControl)
        {
            return;
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
            if (nextWindup <= 2 && nextWindup > 0 && brain.AttackRange <= 64f)
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
            Vector2 cooldownVel = Vector2.Zero;
            if (brain.AttackRange > 64f)
            {
                Vector2 away = pos - atkTargetPos;
                float awayLenSq = away.LengthSquared();
                float kiteThreshold = brain.AttackRange * 0.38f;
                if (awayLenSq > 1e-4f && awayLenSq < (kiteThreshold * kiteThreshold))
                {
                    Vector2 retreatDir = away / MathF.Sqrt(awayLenSq);
                    if (this.HasNavigationClearance(world, pos, pos + (retreatDir * 24f), entityRadius))
                    {
                        cooldownVel = retreatDir * (brain.MoveSpeed * 0.65f);
                    }
                }
            }

            SetEntityVelocity(world, aiEntity, cooldownVel);
            brain = brain with { CooldownRemainingTicks = brain.CooldownRemainingTicks - 1 };
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
        in AiTacticsComponent tactics,
        float directDistSq,
        Vector2 directDelta,
        Span<Entity> neighborScratch)
    {
        Vector2 desiredDir;
        if (!hasClearance && _grid is not null)
        {
            PathWaypointBuffer buffer = pathStore.GetOrAdd(aiEntity, static () => new PathWaypointBuffer());
            if (!buffer.HasPath || brain.RepathCooldownTicks == 0)
            {
                this.TryRepath(buffer, pos, targetPos, ChaseRepathIntervalTicks, ref brain);
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
            float surroundEngage = MathF.Max(brain.AttackRange * 2f, tactics.SurroundRadius * SurroundEngageRingFactor);
            if (tactics.SurroundRadius > 0f && hasClearance && directDistSq <= surroundEngage * surroundEngage)
            {
                // Melee packs claim distinct slots on a ring around the target instead of converging on one point.
                Vector2 slot = AiSteering.ComputeSurroundPoint(
                    world,
                    aiEntity,
                    pos,
                    brain.TargetEntity,
                    targetPos,
                    tactics.SurroundRadius,
                    neighborScratch);
                Vector2 toSlot = slot - pos;
                float toSlotSq = toSlot.LengthSquared();
                if (toSlotSq > 1f)
                {
                    desiredDir = toSlot / MathF.Sqrt(toSlotSq);
                }
            }
            else if (hasClearance && directDistSq > flankThreshold * flankThreshold && desiredDir != Vector2.Zero)
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
        if (!this.HasNavigationClearance(world, pos, homePos, entityRadius) && _grid is not null)
        {
            PathWaypointBuffer buffer = pathStore.GetOrAdd(aiEntity, static () => new PathWaypointBuffer());
            if (!buffer.HasPath || brain.RepathCooldownTicks == 0)
            {
                this.TryRepath(buffer, pos, homePos, ReturnRepathIntervalTicks, ref brain);
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

    private void TryRepath(PathWaypointBuffer buffer, Vector2 from, Vector2 to, uint intervalTicks, ref AiBrainComponent brain)
    {
        if (_grid is null)
        {
            return;
        }

        if (_pathSearchesThisTick >= this.MaxPathSearchesPerTick)
        {
            // Over this tick's CPU budget: keep the previous path (if any) and retry next tick.
            _pathSearchesDeferredThisTick++;
            return;
        }

        _pathSearchesThisTick++;
        int nodeBudget = Math.Clamp(this.MaxPathNodesPerSearch, 1, _pathfinder.MaxNodes);
        _ = buffer.TryComputePath(_pathfinder, _grid, from, to, _tileSize, nodeBudget);
        brain = brain with { RepathCooldownTicks = intervalTicks };
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
        if (destination == default || toDest.LengthSquared() <= 16f || !this.HasNavigationClearance(world, pos, destination, entityRadius))
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
                if (_grid is not null && !_grid.IsWalkable(tx, ty))
                {
                    continue;
                }

                if (this.HasNavigationClearance(world, pos, candidate, entityRadius))
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
        float speedMult = physics is not null ? physics.GetMoveSpeedMultiplier(pos, _terrainMask) : 1f;
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
        in AiTacticsComponent tactics,
        float aggroMultiplier,
        ulong tick,
        Span<Entity> scratch)
    {
        Entity threatTarget = SelectThreatTarget(entities, threatStore, aiEntity, Entity.Null, in tactics, tick);
        if (threatTarget.IsValid)
        {
            return threatTarget;
        }

        return FindClosestTarget(world, aiEntity, origin, brain.AggroRadius * aggroMultiplier, brain.TargetLayerMask, scratch);
    }

    private static Entity SelectThreatTarget(
        EntityManager entities,
        EntityAttachmentStore<ThreatTable> threatStore,
        Entity aiEntity,
        Entity current,
        in AiTacticsComponent tactics,
        ulong tick)
    {
        if (threatStore.TryGet(aiEntity, out ThreatTable? threats))
        {
            return threats.SelectTarget(entities, current, tactics.ThreatSwitchMargin, tick);
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

    private bool HasNavigationClearance(WorldSimulation world, Vector2 from, Vector2 to, float entityRadius)
    {
        Vector2 delta = to - from;
        float dist = delta.Length();
        if (dist <= 1e-3f)
        {
            return true;
        }

        if (TerrainMask.IsWaterBound(_terrainMask) && _grid is not null)
        {
            // A water creature's walls are the tiers it does not live in, which the terrain raycast cannot see.
            return this.HasWaterClearance(from, to, dist);
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

    private bool HasWaterClearance(Vector2 from, Vector2 to, float dist)
    {
        int steps = Math.Max(1, (int)MathF.Ceiling(dist / (_tileSize * 0.5f)));
        for (int step = 1; step <= steps; step++)
        {
            Vector2 at = Vector2.Lerp(from, to, step / (float)steps);
            if (!_grid!.IsWalkable((int)MathF.Floor(at.X / _tileSize), (int)MathF.Floor(at.Y / _tileSize)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidLivingTarget(EntityManager entities, Entity target) =>
        target.IsValid &&
        entities.IsAlive(target) &&
        entities.HasTransform(target) &&
        (!entities.HasCollider(target) || !entities.GetCollider(target).IsTrigger) &&
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
