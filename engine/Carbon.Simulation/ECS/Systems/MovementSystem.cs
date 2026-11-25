// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Physics.Obstacles;
using Carbon.Physics.Shapes;
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

        ComponentPool<Velocity2D> velocities = entities.GetPool<Velocity2D>();
        ComponentPool<Transform2D> transforms = entities.GetPool<Transform2D>();
        ComponentPool<Collider2D> colliders = entities.GetPool<Collider2D>();

        ReadOnlySpan<int> slots = velocities.EntitySlots;
        Span<Velocity2D> velSpan = velocities.AsSpan();

        for (int i = 0; i < slots.Length; i++)
        {
            int slot = slots[i];
            if (!transforms.Has(slot))
            {
                continue;
            }

            Entity entity = entities.GetEntity(slot);
            if (!entity.IsValid)
            {
                continue;
            }

            ref Transform2D transform = ref transforms.Get(slot);
            ref Velocity2D velocity = ref velSpan[i];
            bool hasCollider = colliders.TryGet(slot, out Collider2D collider);

            Vector2 pos = transform.Position;
            Vector2 linear = velocity.Linear;
            bool moved = StepSingle(
                ref pos,
                ref linear,
                velocity.Drag,
                in collider,
                hasCollider,
                dt,
                worldPhysics);

            velocity.Linear = linear;
            if (moved)
            {
                transform.Position = pos;
                markDirty?.Invoke(entity, EntityDirtyMask.Transform);
            }
        }
    }

    /// <summary>
    /// Steps a single entity position and linear velocity over <paramref name="dt"/> using continuous collision
    /// detection (CCD) sub-stepping against <paramref name="worldPhysics"/>.
    /// Shared by both authoritative simulation and client-side prediction reconciliation.
    /// </summary>
    /// <param name="position">Entity world position updated in-place.</param>
    /// <param name="linearVelocity">Entity linear velocity updated in-place for drag and wall sliding.</param>
    /// <param name="drag">Linear damping coefficient.</param>
    /// <param name="collider">Entity collider definition.</param>
    /// <param name="hasCollider">Whether <paramref name="collider"/> is active on the entity.</param>
    /// <param name="dt">Fixed timestep duration in seconds.</param>
    /// <param name="worldPhysics">Optional static world collision provider.</param>
    /// <returns><see langword="true"/> if <paramref name="position"/> changed; otherwise, <see langword="false"/>.</returns>
    public static bool StepSingle(
        ref Vector2 position,
        ref Vector2 linearVelocity,
        float drag,
        in Collider2D collider,
        bool hasCollider,
        float dt,
        IWorldPhysics2D? worldPhysics)
    {
        Vector2 previousPos = position;
        Vector2 displacement = linearVelocity * dt;

        if (drag > 0f)
        {
            float factor = MathF.Max(0f, 1f - (drag * dt));
            linearVelocity *= factor;
        }

        if (worldPhysics is not null && hasCollider && !collider.IsTrigger && collider.Kind != ColliderKind.None)
        {
            float extent = collider.Kind == ColliderKind.Circle
                ? collider.Radius
                : MathF.Min(collider.Box.Width, collider.Box.Height) * 0.5f;

            if (extent > 0f)
            {
                AABB2D localBox = collider.Box;
                float totalDist = displacement.Length();

                if (totalDist > 0.0001f)
                {
                    float maxStep = MathF.Min(2.5f, extent * 0.5f);
                    int subSteps = Math.Clamp((int)MathF.Ceiling(totalDist / maxStep), 1, 16);
                    Vector2 stepDelta = displacement / subSteps;

                    for (int step = 0; step < subSteps; step++)
                    {
                        position += stepDelta;
                        bool hit = collider.Kind == ColliderKind.Circle
                            ? worldPhysics.ResolveCircleCollision(ref position, collider.Radius, out Vector2 pushNormal)
                            : worldPhysics.ResolveAabbCollision(ref position, in localBox, out pushNormal);

                        if (hit)
                        {
                            float stepDot = Vector2.Dot(stepDelta, pushNormal);
                            if (stepDot < 0f)
                            {
                                stepDelta -= pushNormal * stepDot;
                            }

                            float velDot = Vector2.Dot(linearVelocity, pushNormal);
                            if (velDot < 0f)
                            {
                                linearVelocity -= pushNormal * velDot;
                            }
                        }
                    }
                }
                else
                {
                    position += displacement;
                    _ = collider.Kind == ColliderKind.Circle
                        ? worldPhysics.ResolveCircleCollision(ref position, collider.Radius, out _)
                        : worldPhysics.ResolveAabbCollision(ref position, in localBox, out _);
                }

                return position != previousPos;
            }
        }

        position = previousPos + displacement;
        return position != previousPos;
    }
}
