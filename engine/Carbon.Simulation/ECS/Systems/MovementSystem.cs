// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Physics.Obstacles;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.Networking;

namespace Carbon.Simulation.ECS.Systems;

/// <summary>
/// Integrates kinematic movement, applies linear drag damping, and resolves continuous static obstacle collisions
/// via CCD sub-stepping for active entities. Zero heap allocation on hot-path execution.
/// </summary>
public static class MovementSystem
{
    /// <summary>
    /// Updates position and linear velocity across active entities, optionally sub-stepping and sliding against world obstacles.
    /// </summary>
    /// <param name="entities">The entity manager.</param>
    /// <param name="dt">Simulation delta time in seconds.</param>
    /// <param name="worldPhysics">Optional static obstacle / world physics provider.</param>
    public static void Update(EntityManager entities, float dt, IWorldPhysics2D? worldPhysics = null) =>
        Update(entities, dt, worldPhysics, markDirty: null);

    /// <summary>
    /// Updates position and linear velocity across active entities, optionally resolving continuous world collisions
    /// and marking modified transforms dirty for network replication.
    /// </summary>
    /// <param name="entities">The entity manager.</param>
    /// <param name="dt">Simulation delta time in seconds.</param>
    /// <param name="worldPhysics">Optional static obstacle / world physics provider.</param>
    /// <param name="markDirty">Optional callback invoked when an entity's transform changes.</param>
    public static void Update(
        EntityManager entities,
        float dt,
        IWorldPhysics2D? worldPhysics,
        Action<Entity, EntityDirtyMask>? markDirty)
    {
        ArgumentNullException.ThrowIfNull(entities);

        IReadOnlyList<Entity> active = entities.ActiveEntities;
        int count = active.Count;

        for (int i = 0; i < count; i++)
        {
            Entity entity = active[i];
            if (!entities.HasTransform(entity) || !entities.HasVelocity(entity))
            {
                continue;
            }

            ref Transform2D transform = ref entities.GetTransform(entity);
            ref Velocity2D velocity = ref entities.GetVelocity(entity);

            Vector2 previousPos = transform.Position;
            Vector2 displacement = velocity.Linear * dt;

            // Apply drag damping if configured
            if (velocity.Drag > 0f)
            {
                float factor = MathF.Max(0f, 1f - (velocity.Drag * dt));
                velocity.Linear *= factor;
            }

            // Resolve against static world physics using continuous collision detection sub-stepping
            if (worldPhysics is not null && entities.HasCollider(entity))
            {
                ref readonly Collider2D col = ref entities.GetCollider(entity);
                if (!col.IsTrigger && col.Kind != ColliderKind.None)
                {
                    float radius = col.Kind == ColliderKind.Circle
                        ? col.Radius
                        : MathF.Min(col.Box.Width, col.Box.Height) * 0.5f;
                    Vector2 centerOffset = col.Kind == ColliderKind.Aabb ? col.Box.Center : Vector2.Zero;

                    if (radius > 0f)
                    {
                        float totalDist = displacement.Length();
                        if (totalDist > 0.0001f)
                        {
                            float maxStep = MathF.Min(2.5f, radius * 0.5f);
                            int subSteps = Math.Clamp((int)MathF.Ceiling(totalDist / maxStep), 1, 16);
                            Vector2 stepDelta = displacement / subSteps;
                            Vector2 currentCenter = previousPos + centerOffset;

                            for (int step = 0; step < subSteps; step++)
                            {
                                currentCenter += stepDelta;
                                if (worldPhysics.ResolveCircleCollision(ref currentCenter, radius, out Vector2 pushNormal))
                                {
                                    float stepDot = Vector2.Dot(stepDelta, pushNormal);
                                    if (stepDot < 0f)
                                    {
                                        stepDelta -= pushNormal * stepDot;
                                    }

                                    float velDot = Vector2.Dot(velocity.Linear, pushNormal);
                                    if (velDot < 0f)
                                    {
                                        velocity.Linear -= pushNormal * velDot;
                                    }
                                }
                            }

                            transform.Position = currentCenter - centerOffset;
                        }
                        else
                        {
                            Vector2 currentCenter = previousPos + displacement + centerOffset;
                            _ = worldPhysics.ResolveCircleCollision(ref currentCenter, radius, out _);
                            transform.Position = currentCenter - centerOffset;
                        }

                        if (transform.Position != previousPos)
                        {
                            markDirty?.Invoke(entity, EntityDirtyMask.Transform);
                        }

                        continue;
                    }
                }
            }

            transform.Position = previousPos + displacement;
            if (transform.Position != previousPos)
            {
                markDirty?.Invoke(entity, EntityDirtyMask.Transform);
            }
        }
    }
}
