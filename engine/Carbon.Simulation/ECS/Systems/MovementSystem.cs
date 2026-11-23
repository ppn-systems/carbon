// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Physics.Obstacles;
using Carbon.Simulation.ECS.Components;

namespace Carbon.Simulation.ECS.Systems;

/// <summary>
/// Integrates kinematic movement, applies linear drag damping, and resolves static obstacle terrain collisions for active entities.
/// Zero heap allocation on hot-path execution.
/// </summary>
public static class MovementSystem
{
    /// <summary>
    /// Updates position and linear velocity across active entities, optionally sliding against static obstacle tiles.
    /// </summary>
    /// <param name="entities">The entity manager.</param>
    /// <param name="dt">Simulation delta time in seconds.</param>
    /// <param name="obstacleGrid">Optional static obstacle tile grid.</param>
    public static void Update(EntityManager entities, float dt, TileObstacleGrid? obstacleGrid = null)
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

                // Resolve against static obstacle terrain if configured
                if (obstacleGrid != null && entities.HasCollider(entity))
                {
                    ref readonly Collider2D col = ref entities.GetCollider(entity);
                    if (!col.IsTrigger && col.Kind == ColliderKind.Circle)
                    {
                        Vector2 pos = transform.Position;
                        if (obstacleGrid.ResolveCircle(ref pos, col.Radius, out Vector2 pushNormal))
                        {
                            transform.Position = pos;

                            // Smooth wall sliding: zero out the velocity component heading into the wall
                            float dot = Vector2.Dot(velocity.Linear, pushNormal);
                            if (dot < 0f)
                            {
                                velocity.Linear -= dot * pushNormal;
                            }
                        }
                    }
                }
            }
        }
    }
}
