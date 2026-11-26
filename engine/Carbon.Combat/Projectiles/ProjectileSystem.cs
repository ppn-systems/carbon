// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Combat.Damage;
using Carbon.Physics.Shapes;
using Carbon.Simulation.Clock;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.ECS.Systems;
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
                    _ = CombatDamage.Apply(world, proj.Owner, hit.Entity, proj.Damage, proj.IsCritical);
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
