// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Simulation.ECS.Components;

namespace Carbon.Simulation.ECS.Systems;

/// <summary>
/// Integrates kinematic movement and applies linear drag damping to active entities.
/// Zero heap allocation on hot-path execution.
/// </summary>
public static class MovementSystem
{
    /// <summary>
    /// Updates position and linear velocity across all entities having both Transform2D and Velocity2D components.
    /// </summary>
    /// <param name="entities">The entity manager.</param>
    /// <param name="dt">Simulation delta time in seconds.</param>
    public static void Update(EntityManager entities, float dt)
    {
        ArgumentNullException.ThrowIfNull(entities, nameof(entities));

        IReadOnlyList<Entity> active = entities.ActiveEntities;
        int count = active.Count;

        for (int i = 0; i < count; i++)
        {
            Entity entity = active[i];
            if (entities.HasTransform(entity) && entities.HasVelocity(entity))
            {
                ref Transform2D transform = ref entities.GetTransform(entity);
                ref Velocity2D velocity = ref entities.GetVelocity(entity);

                // Integrate position: P = P + (V * dt)
                transform.Position += velocity.Linear * dt;

                // Apply drag damping if configured
                if (velocity.Drag > 0f)
                {
                    float factor = MathF.Max(0f, 1f - (velocity.Drag * dt));
                    velocity.Linear *= factor;
                }
            }
        }
    }
}
