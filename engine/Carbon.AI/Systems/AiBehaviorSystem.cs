// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.AI.Components;
using Carbon.Physics.Obstacles;
using Carbon.Simulation.Clock;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.ECS.Systems;
using Carbon.Simulation.Networking;
using Carbon.Simulation.World;

namespace Carbon.AI.Systems;

/// <summary>
/// Deterministic finite-state machine behavior system driving <see cref="AiBrainComponent"/> entities through
/// <c>Idle / Patrol -> Chase -> Attack -> ReturnHome (Leash)</c>. Executes during <see cref="SimulationPhase.PreUpdate"/>.
/// </summary>
public sealed class AiBehaviorSystem : ISimulationSystem
{
    private const int MaxAggroCandidates = 64;

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
                    Entity target = FindClosestTarget(world, aiEntity, pos, brain.AggroRadius, brain.TargetLayerMask, aggroScratch);
                    if (target.IsValid)
                    {
                        brain = brain with
                        {
                            State = AiState.Chase,
                            TargetEntity = target
                        };
                    }
                    else
                    {
                        SetEntityVelocity(world, aiEntity, Vector2.Zero);
                    }

                    break;

                case AiState.Chase:
                    if (!IsValidLivingTarget(entities, brain.TargetEntity) ||
                        Vector2.DistanceSquared(pos, brain.HomePosition) > (brain.LeashRadius * brain.LeashRadius))
                    {
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

                    if (distSq <= brain.AttackRange * brain.AttackRange)
                    {
                        SetEntityVelocity(world, aiEntity, Vector2.Zero);
                        brain = brain with { State = AiState.Attack };
                    }
                    else
                    {
                        float speedMult = physics is not null ? physics.GetMoveSpeedMultiplier(pos) : 1f;
                        Vector2 dir = distSq > 1e-6f ? (toTarget / MathF.Sqrt(distSq)) : Vector2.Zero;
                        SetEntityVelocity(world, aiEntity, dir * (brain.MoveSpeed * speedMult));
                    }

                    break;

                case AiState.Attack:
                    if (!IsValidLivingTarget(entities, brain.TargetEntity) ||
                        Vector2.DistanceSquared(pos, brain.HomePosition) > (brain.LeashRadius * brain.LeashRadius))
                    {
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
                    if (Vector2.DistanceSquared(pos, atkTargetPos) > (disengageRange * disengageRange))
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
                            State = AiState.Idle,
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

            float dSq = Vector2.DistanceSquared(origin, world.Entities.GetTransform(candidate).Position);
            if (dSq < bestDistSq)
            {
                bestDistSq = dSq;
                best = candidate;
            }
        }

        return best;
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
