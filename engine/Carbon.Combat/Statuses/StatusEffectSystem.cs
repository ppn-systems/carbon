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

            float moveScale = buffer.MoveSpeedScale;

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
                StatusPulseEvent pulse = pulses[p];
                float applied = 0f;
                bool killed = false;
                if (pulse.TotalMagnitude > 0f && entities.HasHealth(entity))
                {
                    ref HealthComponent health = ref entities.GetHealth(entity);
                    if (!health.IsDead && pulse.IsDamage && (controlMask & CrowdControlMask.Invulnerable) == 0)
                    {
                        // ADR-029 D5: a non-lethal effect never takes the last hit point.
                        float floor = pulse.Lethal ? 0f : MathF.Min(1f, health.Current);
                        float nextHp = MathF.Max(floor, health.Current - pulse.TotalMagnitude);
                        applied = health.Current - nextHp;
                        if (applied > 0f)
                        {
                            health = new HealthComponent(nextHp, health.Max);
                            world.MarkDirty(entity, EntityDirtyMask.Health);
                            killed = health.IsDead;
                        }
                    }
                    else if (!health.IsDead && !pulse.IsDamage)
                    {
                        float nextHp = MathF.Min(health.Max, health.Current + pulse.TotalMagnitude);
                        applied = nextHp - health.Current;
                        if (applied > 0f)
                        {
                            health = new HealthComponent(nextHp, health.Max);
                            world.MarkDirty(entity, EntityDirtyMask.Health);
                        }
                    }
                }

                world.PublishEvent(pulse with { Applied = applied });
                if (applied > 0f && pulse.IsDamage)
                {
                    world.PublishEvent(new DamageEvent(pulse.Caster, entity, applied, IsCritical: false));
                    if (killed)
                    {
                        world.PublishEvent(new EntityKilledEvent(pulse.Caster, entity));
                    }
                }
            }
        }
    }
}
