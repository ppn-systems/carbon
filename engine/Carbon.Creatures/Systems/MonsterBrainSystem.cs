// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.AI.Components;
using Carbon.AI.Systems;
using Carbon.Combat.Attacks;
using Carbon.Creatures.Components;
using Carbon.Creatures.Definitions;
using Carbon.Physics.Obstacles;
using Carbon.Physics.Shapes;
using Carbon.Simulation.Clock;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.ECS.Systems;
using Carbon.Simulation.Networking;
using Carbon.Simulation.World;

namespace Carbon.Creatures.Systems;

/// <summary>
/// Combat decision layer for monsters on top of <see cref="AiBehaviorSystem"/>. While the brain is in
/// <see cref="AiState.Attack"/> it picks the first ready attack profile whose range window contains the target,
/// otherwise positions the monster: ranged monsters kite below <see cref="MonsterDefinition.RetreatRange"/>, casters
/// hold mid-distance, melee monsters close in. Attacks are interrupted when the brain leashes home or loses its target.
/// Executes in <see cref="SimulationPhase.PreUpdate"/>; register after <see cref="AiBehaviorSystem"/> and before
/// <see cref="AttackSystem"/>. Zero-allocation per tick.
/// </summary>
public sealed class MonsterBrainSystem : ISimulationSystem
{
    private const float RetreatSpeedFactor = 0.8f;
    private const float RetreatProbeDistance = 24f;

    private readonly MonsterCatalog _catalog;
    private readonly AttackSystem _attacks;

    /// <summary>
    /// Initializes a new instance of the <see cref="MonsterBrainSystem"/> class.
    /// </summary>
    /// <param name="catalog">Monster catalog.</param>
    /// <param name="attacks">Attack system built from <see cref="MonsterCatalog.AttackProfiles"/>.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="attacks"/> was not built from the catalog profiles.</exception>
    public MonsterBrainSystem(MonsterCatalog catalog, AttackSystem attacks)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(attacks);

        if (attacks.ProfileCount != catalog.AttackProfiles.Length)
        {
            throw new ArgumentException("Attack system profiles must be built from the monster catalog.", nameof(attacks));
        }

        _catalog = catalog;
        _attacks = attacks;
    }

    /// <inheritdoc/>
    public SimulationPhase Phase => SimulationPhase.PreUpdate;

    /// <inheritdoc/>
    public void Update(WorldSimulation world, FixedTickArgs tick)
    {
        ArgumentNullException.ThrowIfNull(world);

        EntityManager entities = world.Entities;
        ComponentPool<MonsterComponent> pool = entities.GetPool<MonsterComponent>();
        ReadOnlySpan<int> slots = pool.EntitySlots;
        ReadOnlySpan<MonsterComponent> monsters = pool.AsSpan();
        IWorldPhysics2D? physics = world.WorldPhysics ?? world.ObstacleGrid;

        for (int i = 0; i < slots.Length; i++)
        {
            Entity entity = entities.GetEntity(slots[i]);
            if (!entity.IsValid ||
                !entities.HasTransform(entity) ||
                !entities.HasComponent<AiBrainComponent>(entity) ||
                !entities.TryGetComponent(entity, out AttackStateComponent attack))
            {
                continue;
            }

            AiBrainComponent brain = entities.GetComponent<AiBrainComponent>(entity);
            bool dead = entities.HasHealth(entity) && entities.GetHealth(entity).IsDead;

            if (attack.IsBusy)
            {
                if (dead || brain.State is AiState.ReturnHome or AiState.Idle or AiState.Patrol)
                {
                    _ = _attacks.Cancel(world, entity);
                }

                continue;
            }

            if (dead || brain.State != AiState.Attack || !IsTargetable(entities, brain.TargetEntity))
            {
                continue;
            }

            MonsterComponent monster = monsters[i];
            MonsterDefinition def = _catalog.GetDefinition(monster.MonsterIndex);
            Vector2 pos = entities.GetTransform(entity).Position;
            Vector2 targetPos = entities.GetTransform(brain.TargetEntity).Position;
            Vector2 toTarget = targetPos - pos;
            float dist = toTarget.Length();

            float speedMult = physics is not null ? physics.GetMoveSpeedMultiplier(pos) : 1f;
            Vector2 dir = dist > 1e-3f ? toTarget / dist : Vector2.Zero;
            bool canRetreat = dir != Vector2.Zero && !world.Raycast(new Ray2D(pos, -dir, RetreatProbeDistance), out _);
            Vector2 retreatVelocity = canRetreat ? -dir * (brain.MoveSpeed * RetreatSpeedFactor * speedMult) : Vector2.Zero;

            // Kiters (ranged / casters) restore their spacing first; they only use close-range fallbacks when cornered.
            if (dist < def.RetreatRange && canRetreat)
            {
                SetVelocity(world, entity, retreatVelocity);
                continue;
            }

            _catalog.GetAttackProfileRange(monster.MonsterIndex, out ushort firstProfile, out int profileCount);
            bool needCloser = false;
            bool needFarther = false;
            bool started = false;
            for (int p = 0; p < profileCount && !started; p++)
            {
                ushort profileId = (ushort)(firstProfile + p);
                if (!AttackSystem.IsReady(world, entity, profileId, tick.TickIndex))
                {
                    continue;
                }

                ref readonly AttackProfile profile = ref _attacks.GetProfile(profileId);
                if (dist > profile.MaxRange)
                {
                    needCloser = true;
                }
                else if (dist < profile.MinRange)
                {
                    needFarther = true;
                }
                else
                {
                    started = _attacks.TryBeginAttack(
                        world,
                        entity,
                        profileId,
                        brain.TargetEntity,
                        targetPos,
                        monster.Damage * profile.DamageMultiplier,
                        brain.TargetLayerMask,
                        tick.TickIndex);
                }
            }

            if (started)
            {
                continue;
            }

            Vector2 velocity = Vector2.Zero;
            if (needCloser)
            {
                velocity = dir * (brain.MoveSpeed * speedMult);
            }
            else if (needFarther)
            {
                velocity = retreatVelocity;
            }

            SetVelocity(world, entity, velocity);
        }
    }

    private static bool IsTargetable(EntityManager entities, Entity target) =>
        target.IsValid &&
        entities.IsAlive(target) &&
        entities.HasTransform(target) &&
        (!entities.HasHealth(target) || !entities.GetHealth(target).IsDead);

    private static void SetVelocity(WorldSimulation world, Entity entity, Vector2 linear)
    {
        if (!world.Entities.HasVelocity(entity))
        {
            return;
        }

        ref Velocity2D velocity = ref world.Entities.GetVelocity(entity);
        if (velocity.Linear != linear)
        {
            velocity.Linear = linear;
            world.MarkDirty(entity, EntityDirtyMask.Velocity);
        }
    }
}
