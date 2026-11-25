// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Combat.Components;
using Carbon.Combat.Events;
using Carbon.Combat.Stats;
using Carbon.Physics.Shapes;
using Carbon.Simulation.Clock;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.ECS.Systems;
using Carbon.Simulation.Networking;
using Carbon.Simulation.World;

namespace Carbon.Combat.Projectiles;

/// <summary>
/// Swept-segment projectile hit detection and lifetime management system.
/// Executes during <see cref="SimulationPhase.Physics"/> to prevent high-velocity projectile tunneling.
/// </summary>
public sealed class ProjectileSystem : ISimulationSystem
{
    private readonly List<Entity> _toDespawn = new(32);

    /// <inheritdoc/>
    public SimulationPhase Phase => SimulationPhase.Physics;

    /// <inheritdoc/>
    public void Update(WorldSimulation world, FixedTickArgs tick)
    {
        ArgumentNullException.ThrowIfNull(world);

        _toDespawn.Clear();
        EntityManager entities = world.Entities;
        ComponentPool<ProjectileComponent> pool = entities.GetPool<ProjectileComponent>();
        ReadOnlySpan<int> slots = pool.EntitySlots;
        Span<ProjectileComponent> values = pool.AsSpan();

        for (int i = 0; i < slots.Length; i++)
        {
            Entity projEntity = entities.GetEntity(slots[i]);
            if (!projEntity.IsValid || !entities.HasTransform(projEntity))
            {
                continue;
            }

            ProjectileComponent proj = values[i];
            Vector2 currentPos = entities.GetTransform(projEntity).Position;
            Vector2 startPos = entities.TryGetComponent(projEntity, out PreviousTransform2D prev)
                ? prev.Position
                : currentPos;

            Vector2 segment = currentPos - startPos;
            float dist = segment.Length();

            if (dist > 0.0001f)
            {
                Ray2D ray = new(startPos, segment, dist);
                if (world.RaycastWorld(in ray, out WorldRaycastHit hit, proj.TargetLayerMask, proj.Owner))
                {
                    if (hit.Entity.IsValid && entities.HasHealth(hit.Entity))
                    {
                        bool invulnerable = entities.TryGetComponent(hit.Entity, out CombatStateComponent combatState) &&
                                            combatState.IsInvulnerable;
                        if (!invulnerable)
                        {
                            float effectiveDamage = proj.Damage;
                            EntityAttachmentStore<StatSheet> statsStore = entities.GetAttachmentStore<StatSheet>();
                            if (effectiveDamage > 0f && statsStore.TryGet(hit.Entity, out StatSheet? stats))
                            {
                                float defense = MathF.Max(0f, stats.GetValue(StatType.Defense));
                                effectiveDamage *= 100f / (100f + defense);
                            }

                            ref HealthComponent health = ref entities.GetHealth(hit.Entity);
                            bool wasDead = health.IsDead;
                            float nextHp = MathF.Max(0f, health.Current - effectiveDamage);
                            health = new HealthComponent(nextHp, health.Max);
                            world.MarkDirty(hit.Entity, EntityDirtyMask.Health);

                            world.PublishEvent(new DamageEvent(proj.Owner, hit.Entity, effectiveDamage, proj.IsCritical));
                            if (!wasDead && health.IsDead)
                            {
                                world.PublishEvent(new EntityKilledEvent(proj.Owner, hit.Entity));
                            }
                        }
                    }

                    _toDespawn.Add(projEntity);
                    continue;
                }
            }

            if (proj.RemainingTicks <= 1)
            {
                _toDespawn.Add(projEntity);
            }
            else
            {
                values[i] = proj with { RemainingTicks = proj.RemainingTicks - 1 };
            }
        }

        for (int i = 0; i < _toDespawn.Count; i++)
        {
            world.DespawnEntity(_toDespawn[i]);
        }
    }
}
