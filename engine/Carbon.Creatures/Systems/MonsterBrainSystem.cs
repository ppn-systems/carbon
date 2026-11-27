// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.AI.Components;
using Carbon.AI.Systems;
using Carbon.AI.Tactics;
using Carbon.Combat.Attacks;
using Carbon.Creatures.Components;
using Carbon.Creatures.Definitions;
using Carbon.Physics.Obstacles;
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
/// otherwise positions the monster by <see cref="MonsterBehaviorDefinition.Role"/>: ranged monsters and casters back
/// off below <see cref="MonsterDefinition.RetreatRange"/> (sliding along walls), hold their preferred range and strafe
/// between shots; melee monsters close in to distinct surround slots. Attacks are interrupted when the brain leashes
/// home, flees, or loses its target.
/// Executes in <see cref="SimulationPhase.PreUpdate"/>; register after <see cref="AiBehaviorSystem"/> and before
/// <see cref="AttackSystem"/>. Zero-allocation per tick.
/// </summary>
public sealed class MonsterBrainSystem : ISimulationSystem
{
    private const float RetreatSpeedFactor = 0.8f;
    private const float RetreatProbeDistance = 24f;
    private const float ApproachSpeedFactor = 0.6f;
    private const float SlotHoldSpeedFactor = 0.5f;
    private const float SlotArrivalDistance = 4f;
    private const int MaxSlotNeighbors = 16;
    private const float CrowdRadiusFactor = 2.2f;

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
        Span<Entity> slotScratch = stackalloc Entity[MaxSlotNeighbors];

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
                if (dead || brain.State is AiState.ReturnHome or AiState.Idle or AiState.Patrol or AiState.Flee)
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
            float speed = brain.MoveSpeed * speedMult;
            Vector2 dir = dist > 1e-3f ? toTarget / dist : Vector2.Zero;
            MonsterBehaviorDefinition behavior = def.Behavior;
            float side = StrafeSign(entity, tick.TickIndex, behavior.StrafeSwitchTicks);
            bool canRetreat = AiSteering.TryFindOpenDirection(world, pos, -dir, RetreatProbeDistance, side, out Vector2 retreatDir);
            Vector2 retreatVelocity = canRetreat ? retreatDir * (speed * RetreatSpeedFactor) : Vector2.Zero;

            // Kiters (ranged / casters) restore their spacing first, sliding along walls; they only use close-range
            // fallbacks when cornered.
            if (dist < def.RetreatRange && canRetreat)
            {
                SetVelocity(world, entity, retreatVelocity);
                continue;
            }

            // Kiters only shoot from inside their preferred band unless cornered, so each shot does not cost ground.
            bool kiter = behavior.Role != MonsterRole.Melee;
            float bandMin = kiter ? _catalog.GetPreferredRange(monster.MonsterIndex) - behavior.RangeTolerance : 0f;
            bool holdFire = kiter && canRetreat && dist < bandMin;

            // Crowded melee monsters step to a free surround slot before swinging (no stacking on one tile).
            bool surround = behavior.Role == MonsterRole.Melee && behavior.SurroundRadius > 0f;
            if (surround && IsCrowded(world, entity, pos, in brain, slotScratch))
            {
                Vector2 slotVelocity = SteerToSlot(world, entity, pos, in brain, targetPos, behavior.SurroundRadius, speed, slotScratch, Vector2.Zero);
                if (slotVelocity != Vector2.Zero)
                {
                    SetVelocity(world, entity, slotVelocity);
                    continue;
                }
            }

            _catalog.GetAttackProfileRange(monster.MonsterIndex, out ushort firstProfile, out int profileCount);
            bool needCloser = false;
            bool needFarther = holdFire;
            bool started = false;
            for (int p = 0; p < profileCount && !started && !holdFire; p++)
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

            Vector2 velocity;
            if (needCloser)
            {
                velocity = surround
                    ? SteerToSlot(world, entity, pos, in brain, targetPos, behavior.SurroundRadius, speed, slotScratch, dir)
                    : dir * speed;
            }
            else if (needFarther)
            {
                velocity = retreatVelocity;
            }
            else if (kiter)
            {
                velocity = HoldRange(world, pos, dir, dist, _catalog.GetPreferredRange(monster.MonsterIndex), behavior, side, speed, retreatVelocity);
            }
            else if (surround)
            {
                // Waiting on cooldown: drift to a free slot so the pack spreads instead of stacking on one side.
                velocity = SteerToSlot(world, entity, pos, in brain, targetPos, behavior.SurroundRadius, speed * SlotHoldSpeedFactor, slotScratch, Vector2.Zero);
            }
            else
            {
                velocity = Vector2.Zero;
            }

            SetVelocity(world, entity, velocity);
        }
    }

    private static bool IsCrowded(WorldSimulation world, Entity entity, Vector2 pos, in AiBrainComponent brain, Span<Entity> scratch)
    {
        EntityManager entities = world.Entities;
        float radius = entities.HasCollider(entity) ? entities.GetCollider(entity).Radius : 8f;
        world.OverlapCircle(pos, radius * CrowdRadiusFactor, scratch, out int count);
        for (int i = 0; i < count; i++)
        {
            Entity other = scratch[i];
            if (other != entity &&
                entities.TryGetComponent(other, out AiBrainComponent otherBrain) &&
                otherBrain.TargetEntity == brain.TargetEntity)
            {
                return true;
            }
        }

        return false;
    }

    private static float StrafeSign(Entity entity, ulong tick, uint switchTicks) =>
        (((tick / Math.Max(1U, switchTicks)) + (uint)entity.Id) & 1UL) == 0UL ? 1f : -1f;

    private static Vector2 HoldRange(
        WorldSimulation world,
        Vector2 pos,
        Vector2 dir,
        float dist,
        float preferredRange,
        MonsterBehaviorDefinition behavior,
        float side,
        float speed,
        Vector2 retreatVelocity)
    {
        if (dir == Vector2.Zero)
        {
            return Vector2.Zero;
        }

        if (dist > preferredRange + behavior.RangeTolerance)
        {
            return dir * (speed * ApproachSpeedFactor);
        }

        if (dist < preferredRange - behavior.RangeTolerance)
        {
            return retreatVelocity;
        }

        if (behavior.StrafeSpeedFactor <= 0f)
        {
            return Vector2.Zero;
        }

        // Circle-strafe around the target; flip to the other side when a wall blocks this one.
        Vector2 tangent = new Vector2(-dir.Y, dir.X) * side;
        return AiSteering.TryFindOpenDirection(world, pos, tangent, RetreatProbeDistance, -side, out Vector2 strafeDir)
            ? strafeDir * (speed * behavior.StrafeSpeedFactor)
            : Vector2.Zero;
    }

    private static Vector2 SteerToSlot(
        WorldSimulation world,
        Entity entity,
        Vector2 pos,
        in AiBrainComponent brain,
        Vector2 targetPos,
        float ringRadius,
        float speed,
        Span<Entity> scratch,
        Vector2 fallback)
    {
        Vector2 slot = AiSteering.ComputeSurroundPoint(world, entity, pos, brain.TargetEntity, targetPos, ringRadius, scratch);
        Vector2 toSlot = slot - pos;
        float lenSq = toSlot.LengthSquared();
        if (lenSq <= SlotArrivalDistance * SlotArrivalDistance)
        {
            return fallback == Vector2.Zero ? Vector2.Zero : fallback * speed;
        }

        return toSlot * (speed / MathF.Sqrt(lenSq));
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
