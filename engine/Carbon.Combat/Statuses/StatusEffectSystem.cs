// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Combat.Components;
using Carbon.Combat.Events;
using Carbon.Simulation.Clock;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.ECS.Systems;
using Carbon.Simulation.Networking;
using Carbon.Simulation.World;

namespace Carbon.Combat.Statuses;

/// <summary>
/// Simulation system ticking per-entity <see cref="StatusEffectBuffer"/> attachments during
/// <see cref="SimulationPhase.PreUpdate"/>, synchronizing <see cref="CombatStateComponent"/> crowd-control state
/// and applying periodic damage/healing pulses. Zero heap allocations on the hot path.
/// </summary>
public sealed class StatusEffectSystem : ISimulationSystem
{
    private const int MaxPulsesPerEntity = 32;
    private const float SlowMoveSpeedScale = 0.5f;

    /// <inheritdoc/>
    public SimulationPhase Phase => SimulationPhase.PreUpdate;

    /// <inheritdoc/>
    public void Update(WorldSimulation world, FixedTickArgs tick)
    {
        ArgumentNullException.ThrowIfNull(world);

        EntityManager entities = world.Entities;
        EntityAttachmentStore<StatusEffectBuffer> store = entities.GetAttachmentStore<StatusEffectBuffer>();
        ReadOnlySpan<Entity> active = entities.ActiveEntitiesSpan;
        Span<StatusPulseEvent> pulses = stackalloc StatusPulseEvent[MaxPulsesPerEntity];

        for (int i = 0; i < active.Length; i++)
        {
            Entity entity = active[i];
            if (!store.TryGet(entity, out StatusEffectBuffer? buffer))
            {
                continue;
            }

            buffer.AdvanceTick(entity, pulses, out int pulseCount);
            CrowdControlMask controlMask = buffer.ActiveControlMask;

            float moveScale = (controlMask & (CrowdControlMask.Stunned | CrowdControlMask.Rooted)) != 0
                ? 0f
                : (controlMask & CrowdControlMask.Slowed) != 0
                    ? SlowMoveSpeedScale
                    : 1f;

            entities.SetComponent(entity, new CombatStateComponent(controlMask, moveScale));

            if (moveScale == 0f && entities.HasVelocity(entity))
            {
                ref Velocity2D vel = ref entities.GetVelocity(entity);
                if (vel.Linear != Vector2.Zero)
                {
                    vel.Linear = Vector2.Zero;
                    world.MarkDirty(entity, EntityDirtyMask.Velocity);
                }
            }

            for (int p = 0; p < pulseCount; p++)
            {
                ref readonly StatusPulseEvent pulse = ref pulses[p];
                world.PublishEvent(in pulse);

                if (pulse.TotalMagnitude <= 0f || !entities.HasHealth(entity))
                {
                    continue;
                }

                ref HealthComponent health = ref entities.GetHealth(entity);
                if (health.IsDead)
                {
                    continue;
                }

                if (pulse.IsDamage)
                {
                    if ((controlMask & CrowdControlMask.Invulnerable) != 0)
                    {
                        continue;
                    }

                    float nextHp = MathF.Max(0f, health.Current - pulse.TotalMagnitude);
                    health = new HealthComponent(nextHp, health.Max);
                    world.MarkDirty(entity, EntityDirtyMask.Health);
                    world.PublishEvent(new DamageEvent(pulse.Caster, entity, pulse.TotalMagnitude, IsCritical: false));

                    if (health.IsDead)
                    {
                        world.PublishEvent(new EntityKilledEvent(pulse.Caster, entity));
                    }
                }
                else
                {
                    float nextHp = MathF.Min(health.Max, health.Current + pulse.TotalMagnitude);
                    if (nextHp != health.Current)
                    {
                        health = new HealthComponent(nextHp, health.Max);
                        world.MarkDirty(entity, EntityDirtyMask.Health);
                    }
                }
            }
        }
    }
}
