// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Physics.Contacts;
using Carbon.Physics.Narrowphase;
using Carbon.Physics.Separation;
using Carbon.Physics.Shapes;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.Events;
using Carbon.Simulation.Networking;
using Carbon.Simulation.Spatial;

namespace Carbon.Simulation.ECS.Systems;

/// <summary>
/// Resolves entity-to-entity physical collisions (Circle and AABB) and trigger overlap events using the spatial hash
/// grid broadphase. Hot path zero heap allocation using stack scratch buffers.
/// </summary>
public static class EntityCollisionSystem
{
    private const int MaxQueryBuffer = 256;

    /// <summary>
    /// Synchronizes entity positions into the spatial hash grid and resolves entity-to-entity overlapping collisions.
    /// </summary>
    /// <param name="entities">The entity manager.</param>
    /// <param name="spatialGrid">The spatial partitioning grid.</param>
    public static void Update(EntityManager entities, SpatialHashGrid2D spatialGrid) =>
        Update(entities, spatialGrid, triggerEvents: null, markDirty: null);

    /// <summary>
    /// Synchronizes entity positions into the spatial hash grid, resolves solid collisions (Circle and AABB),
    /// emits <see cref="TriggerOverlapEvent"/> notifications for trigger colliders, and marks moved entities dirty.
    /// </summary>
    /// <param name="entities">The entity manager.</param>
    /// <param name="spatialGrid">The spatial partitioning grid.</param>
    /// <param name="triggerEvents">Optional event buffer receiving trigger overlap notifications.</param>
    /// <param name="markDirty">Optional callback invoked when entity transforms change due to separation.</param>
    public static void Update(
        EntityManager entities,
        SpatialHashGrid2D spatialGrid,
        SimulationEventBuffer<TriggerOverlapEvent>? triggerEvents,
        Action<Entity, EntityDirtyMask>? markDirty = null)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(spatialGrid);

        IReadOnlyList<Entity> active = entities.ActiveEntities;
        int count = active.Count;

        // Step 1: Synchronize spatial grid positions
        for (int i = 0; i < count; i++)
        {
            Entity entity = active[i];
            if (entities.HasTransform(entity) && entities.HasCollider(entity))
            {
                ref readonly Transform2D transform = ref entities.GetTransform(entity);
                _ = spatialGrid.Update(entity.Id, transform.Position);
            }
        }

        // Step 2: Broadphase & Narrowphase entity-to-entity resolution
        Span<int> nearbyBuffer = stackalloc int[MaxQueryBuffer];

        for (int i = 0; i < count; i++)
        {
            Entity entityA = active[i];
            if (!entities.HasTransform(entityA) || !entities.HasCollider(entityA))
            {
                continue;
            }

            ref readonly Collider2D colA = ref entities.GetCollider(entityA);
            if (colA.Kind == ColliderKind.None)
            {
                continue;
            }

            ref Transform2D transA = ref entities.GetTransform(entityA);
            CellCoord cell = spatialGrid.WorldToCell(transA.Position);

            spatialGrid.QueryCellRadius(in cell, cellRadius: 1, nearbyBuffer, out int nearbyCount);

            for (int k = 0; k < nearbyCount; k++)
            {
                int otherId = nearbyBuffer[k];

                // Avoid duplicate pairs and self-collision: only test when entityA.Id < otherId
                if (entityA.Id >= otherId)
                {
                    continue;
                }

                Entity entityB = entities.GetEntity(otherId);
                if (!entityB.IsValid || !entities.HasTransform(entityB) || !entities.HasCollider(entityB))
                {
                    continue;
                }

                ref readonly Collider2D colB = ref entities.GetCollider(entityB);
                if (colB.Kind == ColliderKind.None)
                {
                    continue;
                }

                // Check collision layer filtering
                if ((colA.Mask & colB.Layer) == 0 || (colB.Mask & colA.Layer) == 0)
                {
                    continue;
                }

                ref Transform2D transB = ref entities.GetTransform(entityB);

                if (colA.IsTrigger || colB.IsTrigger)
                {
                    if (triggerEvents is not null && TestOverlap(transA.Position, in colA, transB.Position, in colB))
                    {
                        if (colA.IsTrigger)
                        {
                            triggerEvents.Publish(new TriggerOverlapEvent(entityA, entityB));
                        }

                        if (colB.IsTrigger)
                        {
                            triggerEvents.Publish(new TriggerOverlapEvent(entityB, entityA));
                        }
                    }

                    continue;
                }

                Vector2 posA = transA.Position;
                Vector2 posB = transB.Position;

                if (ResolveSolidOverlap(ref posA, in colA, ref posB, in colB))
                {
                    transA.Position = posA;
                    transB.Position = posB;

                    _ = spatialGrid.Update(entityA.Id, posA);
                    _ = spatialGrid.Update(entityB.Id, posB);

                    markDirty?.Invoke(entityA, EntityDirtyMask.Transform);
                    markDirty?.Invoke(entityB, EntityDirtyMask.Transform);
                }
            }
        }
    }

    private static bool TestOverlap(Vector2 posA, in Collider2D colA, Vector2 posB, in Collider2D colB)
    {
        CollisionContact contact;
        if (colA.Kind == ColliderKind.Circle && colB.Kind == ColliderKind.Circle)
        {
            return CollisionDetector.TestCircleCircle(new Circle2D(posA, colA.Radius), new Circle2D(posB, colB.Radius), out contact) && contact.HasCollision;
        }

        if (colA.Kind == ColliderKind.Aabb && colB.Kind == ColliderKind.Aabb)
        {
            return CollisionDetector.TestAabbAabb(colA.Box.Offset(posA), colB.Box.Offset(posB), out contact) && contact.HasCollision;
        }

        if (colA.Kind == ColliderKind.Circle && colB.Kind == ColliderKind.Aabb)
        {
            return CollisionDetector.TestCircleAabb(new Circle2D(posA, colA.Radius), colB.Box.Offset(posB), out contact) && contact.HasCollision;
        }

        return CollisionDetector.TestCircleAabb(new Circle2D(posB, colB.Radius), colA.Box.Offset(posA), out contact) && contact.HasCollision;
    }

    private static bool ResolveSolidOverlap(ref Vector2 posA, in Collider2D colA, ref Vector2 posB, in Collider2D colB)
    {
        if (colA.Kind == ColliderKind.Circle && colB.Kind == ColliderKind.Circle)
        {
            return EntitySeparation.ResolveCircleOverlap(ref posA, ref posB, colA.Radius, colB.Radius, separationRatio: 0.5f);
        }

        if (colA.Kind == ColliderKind.Aabb && colB.Kind == ColliderKind.Aabb)
        {
            AABB2D boxA = colA.Box;
            AABB2D boxB = colB.Box;
            return EntitySeparation.ResolveAabbOverlap(ref posA, in boxA, ref posB, in boxB, separationRatio: 0.5f);
        }

        if (colA.Kind == ColliderKind.Circle && colB.Kind == ColliderKind.Aabb)
        {
            AABB2D boxB = colB.Box;
            return EntitySeparation.ResolveCircleAabbOverlap(ref posA, colA.Radius, ref posB, in boxB, separationRatio: 0.5f);
        }

        AABB2D boxAOnly = colA.Box;
        return EntitySeparation.ResolveCircleAabbOverlap(ref posB, colB.Radius, ref posA, in boxAOnly, separationRatio: 0.5f);
    }
}
