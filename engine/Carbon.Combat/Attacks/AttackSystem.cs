// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Combat.Components;
using Carbon.Combat.Cooldowns;
using Carbon.Combat.Hitboxes;
using Carbon.Simulation.Clock;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.ECS.Systems;
using Carbon.Simulation.Networking;
using Carbon.Simulation.World;

namespace Carbon.Combat.Attacks;

/// <summary>
/// Server-authoritative attack timeline executor. Advances every busy <see cref="AttackStateComponent"/> through
/// <c>Windup -> Travel -> Active -> Recovery</c>, drives lunge / leap velocity, resolves hits only during
/// <see cref="AttackPhase.Active"/>, spawns projectiles, and publishes <see cref="AttackPhaseChangedEvent"/> on every
/// phase start. Executes during <see cref="SimulationPhase.PreUpdate"/>; register it after the systems that start
/// attacks so the start tick counts as the first windup tick. Zero-allocation per tick.
/// </summary>
public sealed class AttackSystem : ISimulationSystem
{
    private const int MaxAreaTargets = 32;

    private readonly AttackProfile[] _profiles;
    private readonly IAttackEffects _effects;

    /// <summary>
    /// Initializes a new instance of the <see cref="AttackSystem"/> class with an immutable profile table.
    /// </summary>
    /// <param name="profiles">Attack profiles indexed by <see cref="AttackStateComponent.ProfileId"/>.</param>
    /// <param name="effects">Hit / projectile side effects (defaults to <see cref="DefaultAttackEffects"/>).</param>
    /// <exception cref="ArgumentException">Thrown when a profile fails <see cref="AttackProfile.Validate"/>.</exception>
    public AttackSystem(ReadOnlySpan<AttackProfile> profiles, IAttackEffects? effects = null)
    {
        _effects = effects ?? DefaultAttackEffects.Instance;
        ArgumentOutOfRangeException.ThrowIfGreaterThan(profiles.Length, ushort.MaxValue + 1);
        for (int i = 0; i < profiles.Length; i++)
        {
            profiles[i].Validate();
        }

        _profiles = profiles.ToArray();
    }

    /// <inheritdoc/>
    public SimulationPhase Phase => SimulationPhase.PreUpdate;

    /// <summary>Gets the number of registered profiles.</summary>
    public int ProfileCount => _profiles.Length;

    /// <summary>
    /// Gets a registered profile by index.
    /// </summary>
    /// <param name="profileId">Profile index.</param>
    /// <returns>The profile.</returns>
    public ref readonly AttackProfile GetProfile(ushort profileId) => ref _profiles[profileId];

    /// <summary>
    /// Determines whether <paramref name="attacker"/> is idle and <paramref name="profileId"/> is off cooldown.
    /// </summary>
    /// <param name="world">Owning world simulation.</param>
    /// <param name="attacker">Attacking entity.</param>
    /// <param name="profileId">Profile index.</param>
    /// <param name="currentTick">Current simulation tick.</param>
    /// <returns><see langword="true"/> if the attack can start now.</returns>
    public static bool IsReady(WorldSimulation world, Entity attacker, ushort profileId, ulong currentTick)
    {
        ArgumentNullException.ThrowIfNull(world);

        if (world.Entities.TryGetComponent(attacker, out AttackStateComponent state) && state.IsBusy)
        {
            return false;
        }

        return !world.Entities.GetAttachmentStore<CooldownTracker>().TryGet(attacker, out CooldownTracker? cooldowns) ||
               cooldowns.IsReady(profileId, currentTick);
    }

    /// <summary>
    /// Starts attack <paramref name="profileId"/> toward <paramref name="targetPosition"/>, locking origin, direction,
    /// and (for spells and leaps) the target point. Starts the profile cooldown and publishes the first phase event.
    /// </summary>
    /// <param name="world">Owning world simulation.</param>
    /// <param name="attacker">Attacking entity (must have a transform).</param>
    /// <param name="profileId">Profile index.</param>
    /// <param name="target">Targeted entity (used for projectile aim tracking; may be <see cref="Entity.Null"/>).</param>
    /// <param name="targetPosition">Aim point in world coordinates.</param>
    /// <param name="damage">Damage dealt to each struck target.</param>
    /// <param name="targetLayerMask">Collider layer mask of valid victims.</param>
    /// <param name="currentTick">Current simulation tick.</param>
    /// <returns><see langword="true"/> if the attack started.</returns>
    public bool TryBeginAttack(
        WorldSimulation world,
        Entity attacker,
        ushort profileId,
        Entity target,
        Vector2 targetPosition,
        float damage,
        uint targetLayerMask,
        ulong currentTick)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(profileId, _profiles.Length);

        EntityManager entities = world.Entities;
        if (!entities.IsAlive(attacker) || !entities.HasTransform(attacker) || !IsReady(world, attacker, profileId, currentTick))
        {
            return false;
        }

        ref readonly AttackProfile profile = ref _profiles[profileId];
        if (entities.TryGetComponent(attacker, out CombatStateComponent combat) &&
            (profile.Kind == AttackKind.Spell ? !combat.CanCast : !combat.CanAct))
        {
            return false;
        }

        Vector2 origin = entities.GetTransform(attacker).Position;
        Vector2 delta = targetPosition - origin;
        float distSq = delta.LengthSquared();
        Vector2 direction = distSq > 1e-6f ? delta / MathF.Sqrt(distSq) : Vector2.UnitX;
        Vector2 locked = targetPosition;
        if (profile.Kind == AttackKind.Leap && distSq > profile.MaxRange * profile.MaxRange)
        {
            locked = origin + (direction * profile.MaxRange);
        }

        AttackStateComponent state = new(
            AttackPhase.None,
            profile.Kind,
            profileId,
            PhaseElapsedTicks: 0,
            PhaseDurationTicks: 0,
            TotalElapsedTicks: 0,
            TotalDurationTicks: profile.TotalTicks,
            target,
            origin,
            locked,
            direction,
            damage,
            targetLayerMask,
            default);

        entities.GetAttachmentStore<CooldownTracker>()
            .GetOrAdd(attacker, static () => new CooldownTracker())
            .StartCooldown(profileId, currentTick, profile.CooldownTicks);

        state = EnterNextPhase(world, attacker, in state, in profile);
        entities.SetComponent(attacker, in state);
        return true;
    }

    /// <summary>
    /// Interrupts the attack in progress on <paramref name="attacker"/> (publishing an interrupted
    /// <see cref="AttackPhase.None"/> event). The cooldown already started is kept.
    /// </summary>
    /// <param name="world">Owning world simulation.</param>
    /// <param name="attacker">Attacking entity.</param>
    /// <returns><see langword="true"/> if an attack was interrupted.</returns>
    public bool Cancel(WorldSimulation world, Entity attacker)
    {
        ArgumentNullException.ThrowIfNull(world);

        if (!world.Entities.HasComponent<AttackStateComponent>(attacker))
        {
            return false;
        }

        ref AttackStateComponent state = ref world.Entities.GetComponent<AttackStateComponent>(attacker);
        if (!state.IsBusy)
        {
            return false;
        }

        state = this.Interrupt(world, attacker, in state);
        return true;
    }

    /// <inheritdoc/>
    public void Update(WorldSimulation world, FixedTickArgs tick)
    {
        ArgumentNullException.ThrowIfNull(world);

        EntityManager entities = world.Entities;
        ComponentPool<AttackStateComponent> pool = entities.GetPool<AttackStateComponent>();
        ReadOnlySpan<int> slots = pool.EntitySlots;
        Span<AttackStateComponent> states = pool.AsSpan();

        for (int i = 0; i < slots.Length; i++)
        {
            AttackStateComponent state = states[i];
            if (!state.IsBusy)
            {
                continue;
            }

            Entity attacker = entities.GetEntity(slots[i]);
            if (!attacker.IsValid || !entities.HasTransform(attacker))
            {
                continue;
            }

            if (entities.TryGetComponent(attacker, out CombatStateComponent combat) && !combat.CanAct)
            {
                states[i] = this.Interrupt(world, attacker, in state);
                continue;
            }

            ref readonly AttackProfile profile = ref _profiles[state.ProfileId];
            if (state.PhaseElapsedTicks >= state.PhaseDurationTicks)
            {
                state = EnterNextPhase(world, attacker, in state, in profile);
                if (!state.IsBusy)
                {
                    SetVelocity(world, attacker, Vector2.Zero);
                    states[i] = state;
                    continue;
                }
            }

            state = state with
            {
                PhaseElapsedTicks = state.PhaseElapsedTicks + 1,
                TotalElapsedTicks = state.TotalElapsedTicks + 1
            };
            states[i] = this.ExecutePhase(world, attacker, in state, in profile, tick.DeltaTime);
        }
    }

    private AttackStateComponent ExecutePhase(
        WorldSimulation world,
        Entity attacker,
        in AttackStateComponent state,
        in AttackProfile profile,
        float deltaTime)
    {
        EntityManager entities = world.Entities;
        Vector2 pos = entities.GetTransform(attacker).Position;

        switch (state.Phase)
        {
            case AttackPhase.Windup:
                SetVelocity(world, attacker, Vector2.Zero);
                if (state.Kind == AttackKind.Projectile &&
                    state.Target.IsValid &&
                    entities.IsAlive(state.Target) &&
                    entities.HasTransform(state.Target))
                {
                    // Aim tracks the target until release.
                    Vector2 aim = entities.GetTransform(state.Target).Position;
                    Vector2 toAim = aim - pos;
                    float lenSq = toAim.LengthSquared();
                    return state with
                    {
                        TargetPosition = aim,
                        Direction = lenSq > 1e-6f ? toAim / MathF.Sqrt(lenSq) : state.Direction
                    };
                }

                return state;

            case AttackPhase.Travel:
                float travelSeconds = profile.TravelTicks * deltaTime;
                Vector2 travelVelocity = Vector2.Zero;
                if (travelSeconds > 0f)
                {
                    if (state.Kind == AttackKind.Melee)
                    {
                        travelVelocity = state.Direction * (profile.LungeDistance / travelSeconds);
                    }
                    else if (state.Kind == AttackKind.Leap)
                    {
                        travelVelocity = (state.TargetPosition - state.Origin) / travelSeconds;
                    }
                }

                SetVelocity(world, attacker, travelVelocity);
                return state;

            case AttackPhase.Active:
                SetVelocity(world, attacker, Vector2.Zero);
                return state.Kind switch
                {
                    AttackKind.Melee => this.ResolveArc(world, attacker, in state, pos, profile.AreaRadius, profile.HalfAngleRadians),
                    AttackKind.Projectile when state.PhaseElapsedTicks == 1 => this.LaunchProjectile(world, attacker, in state, in profile, pos, deltaTime),
                    AttackKind.Spell when state.PhaseElapsedTicks == 1 => profile.Shape == AttackAreaShape.Cone
                        ? this.ResolveArc(world, attacker, in state, pos, profile.AreaRadius, profile.HalfAngleRadians)
                        : this.ResolveCircle(world, attacker, in state, state.TargetPosition, profile.AreaRadius),
                    AttackKind.Leap when state.PhaseElapsedTicks == 1 => this.ResolveCircle(world, attacker, in state, pos, profile.AreaRadius),
                    _ => state
                };

            case AttackPhase.None:
            case AttackPhase.Recovery:
            default:
                SetVelocity(world, attacker, Vector2.Zero);
                return state;
        }
    }

    private AttackStateComponent ResolveArc(
        WorldSimulation world,
        Entity attacker,
        in AttackStateComponent state,
        Vector2 origin,
        float radius,
        float halfAngle)
    {
        Span<Entity> candidates = stackalloc Entity[MaxAreaTargets];
        HitboxQuery.QueryArc(world, origin, state.Direction, radius, halfAngle, candidates, out int count, state.TargetLayerMask, attacker);
        return this.ApplyHits(world, attacker, in state, candidates[..count]);
    }

    private AttackStateComponent ResolveCircle(
        WorldSimulation world,
        Entity attacker,
        in AttackStateComponent state,
        Vector2 center,
        float radius)
    {
        Span<Entity> candidates = stackalloc Entity[MaxAreaTargets];
        world.OverlapCircle(center, radius, candidates, out int count, state.TargetLayerMask);
        return this.ApplyHits(world, attacker, in state, candidates[..count]);
    }

    private AttackStateComponent ApplyHits(
        WorldSimulation world,
        Entity attacker,
        in AttackStateComponent state,
        ReadOnlySpan<Entity> candidates)
    {
        AttackHitList hits = state.Hits;
        for (int i = 0; i < candidates.Length && !hits.IsFull; i++)
        {
            Entity victim = candidates[i];
            if (victim == attacker || hits.Contains(victim))
            {
                continue;
            }

            if (_effects.ApplyHit(world, attacker, victim, state.Damage))
            {
                hits = hits.With(victim);
            }
        }

        return state with { Hits = hits };
    }

    private AttackStateComponent LaunchProjectile(
        WorldSimulation world,
        Entity attacker,
        in AttackStateComponent state,
        in AttackProfile profile,
        Vector2 pos,
        float deltaTime)
    {
        EntityManager entities = world.Entities;
        float attackerRadius = entities.HasCollider(attacker) ? entities.GetCollider(attacker).Radius : 0f;
        Vector2 spawn = pos + (state.Direction * (attackerRadius + profile.ProjectileRadius + 1f));
        Vector2 velocity = state.Direction * profile.ProjectileSpeed;
        float stepDistance = MathF.Max(1e-3f, profile.ProjectileSpeed * MathF.Max(deltaTime, 1e-3f));
        uint flightTicks = (uint)MathF.Ceiling(Vector2.Distance(spawn, state.TargetPosition) / stepDistance);
        uint lifetimeTicks = (uint)MathF.Ceiling(profile.MaxRange * 1.25f / stepDistance) + 1U;

        ProjectileLaunchRequest request = new(
            attacker,
            spawn,
            velocity,
            state.Damage,
            state.TargetLayerMask,
            lifetimeTicks,
            profile.ProjectileRadius,
            profile.ProjectileVisual);
        Entity projectile = _effects.LaunchProjectile(world, in request);
        world.PublishEvent(new ProjectileLaunchedEvent(attacker, projectile, spawn, velocity, profile.ArcHeight, flightTicks));
        return state;
    }

    private static AttackStateComponent EnterNextPhase(
        WorldSimulation world,
        Entity attacker,
        in AttackStateComponent state,
        in AttackProfile profile)
    {
        AttackPhase next = state.Phase;
        uint duration = 0;
        while (next != AttackPhase.Recovery)
        {
            next = next == AttackPhase.None ? AttackPhase.Windup : (AttackPhase)((byte)next + 1);
            duration = profile.GetPhaseTicks(next);
            if (duration > 0)
            {
                break;
            }
        }

        if (duration == 0)
        {
            next = AttackPhase.None;
        }

        AttackStateComponent updated = state with
        {
            Phase = next,
            PhaseElapsedTicks = 0,
            PhaseDurationTicks = duration
        };
        Publish(world, attacker, in updated, in profile, interrupted: false);
        return updated;
    }

    private AttackStateComponent Interrupt(WorldSimulation world, Entity attacker, in AttackStateComponent state)
    {
        SetVelocity(world, attacker, Vector2.Zero);
        AttackStateComponent updated = state with { Phase = AttackPhase.None, PhaseElapsedTicks = 0, PhaseDurationTicks = 0 };
        Publish(world, attacker, in updated, in _profiles[state.ProfileId], interrupted: true);
        return updated;
    }

    private static void Publish(
        WorldSimulation world,
        Entity attacker,
        in AttackStateComponent state,
        in AttackProfile profile,
        bool interrupted)
    {
        float progress = state.Phase == AttackPhase.None && !interrupted ? 1f : state.TotalProgress;
        world.PublishEvent(new AttackPhaseChangedEvent(
            attacker,
            state.Kind,
            state.Phase,
            state.ProfileId,
            state.Origin,
            state.TargetPosition,
            state.Direction,
            profile.AreaRadius,
            profile.HalfAngleRadians,
            state.Kind == AttackKind.Leap ? AttackAreaShape.Circle : (state.Kind == AttackKind.Melee ? AttackAreaShape.Cone : profile.Shape),
            state.PhaseDurationTicks,
            progress,
            interrupted));
    }

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
