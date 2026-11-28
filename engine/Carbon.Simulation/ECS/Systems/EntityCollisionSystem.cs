// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Buffers;
using System.Numerics;
using Carbon.Physics.Contacts;
using Carbon.Physics.Narrowphase;
using Carbon.Physics.Obstacles;
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
    /// <param name="worldPhysics">
    /// World geometry the separated bodies are resolved against again (DET-3): movement has already kept them out of walls,
    /// and separation must not push them back in. <see langword="null"/> separates without world geometry.
    /// </param>
    public static void Update(
        EntityManager entities,
        SpatialHashGrid2D spatialGrid,
        SimulationEventBuffer<TriggerOverlapEvent>? triggerEvents,
        Action<Entity, EntityDirtyMask>? markDirty = null,
        IWorldPhysics2D? worldPhysics = null)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(spatialGrid);

        ComponentPool<Collider2D> colliders = entities.GetPool<Collider2D>();
        ComponentPool<Transform2D> transforms = entities.GetPool<Transform2D>();
        ReadOnlySpan<int> slots = colliders.EntitySlots;
        ReadOnlySpan<Collider2D> colSpan = colliders.AsSpan();

        // Step 1: Synchronize multi-cell spatial grid bounds
        for (int i = 0; i < slots.Length; i++)
        {
            int slot = slots[i];
            if (colSpan[i].Kind == ColliderKind.None || !transforms.Has(slot))
            {
                continue;
            }

            AABB2D worldBounds = colSpan[i].GetWorldAabb(transforms.Get(slot).Position);
            _ = spatialGrid.Update(slot, in worldBounds);
        }

        // Step 2: Broadphase & Narrowphase entity-to-entity resolution
        int[]? rentedBuffer = spatialGrid.EntityCount > MaxQueryBuffer
            ? ArrayPool<int>.Shared.Rent(spatialGrid.EntityCount)
            : null;
        try
        {
            Span<int> nearbyBuffer = rentedBuffer is not null
                ? rentedBuffer
                : stackalloc int[MaxQueryBuffer];

            for (int i = 0; i < slots.Length; i++)
            {
                int slotA = slots[i];
                Collider2D colA = colSpan[i];
                if (colA.Kind == ColliderKind.None || !transforms.Has(slotA))
                {
                    continue;
                }

                Entity entityA = entities.GetEntity(slotA);
                if (!entityA.IsValid)
                {
                    continue;
                }

                ref Transform2D transA = ref transforms.Get(slotA);
                AABB2D worldBoundsA = colA.GetWorldAabb(transA.Position);
                CellCoord minCell = spatialGrid.WorldToCell(worldBoundsA.Min);
                CellCoord maxCell = spatialGrid.WorldToCell(worldBoundsA.Max);
                CellCoord queryMin = new(minCell.X - 1, minCell.Y - 1);
                CellCoord queryMax = new(maxCell.X + 1, maxCell.Y + 1);

                spatialGrid.QueryRegion(in queryMin, in queryMax, nearbyBuffer, out int nearbyCount);

                for (int k = 0; k < nearbyCount; k++)
                {
                    int otherId = nearbyBuffer[k];

                    // Avoid duplicate pairs and self-collision: only test when entityA.Id < otherId
                    if (slotA >= otherId || !transforms.Has(otherId) || !colliders.TryGet(otherId, out Collider2D colB) || colB.Kind == ColliderKind.None)
                    {
                        continue;
                    }

                    Entity entityB = entities.GetEntity(otherId);
                    if (!entityB.IsValid)
                    {
                        continue;
                    }

                    // Check collision layer filtering
                    if ((colA.Mask & colB.Layer) == 0 || (colB.Mask & colA.Layer) == 0)
                    {
                        continue;
                    }

                    ref Transform2D transB = ref transforms.Get(otherId);

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
                        if (worldPhysics is not null)
                        {
                            ResolveAgainstWorld(worldPhysics, ref posA, in colA);
                            ResolveAgainstWorld(worldPhysics, ref posB, in colB);
                        }

                        transA.Position = posA;
                        transB.Position = posB;

                        AABB2D updatedBoundsA = colA.GetWorldAabb(posA);
                        AABB2D updatedBoundsB = colB.GetWorldAabb(posB);
                        _ = spatialGrid.Update(slotA, in updatedBoundsA);
                        _ = spatialGrid.Update(otherId, in updatedBoundsB);

                        markDirty?.Invoke(entityA, EntityDirtyMask.Transform);
                        markDirty?.Invoke(entityB, EntityDirtyMask.Transform);
                    }
                }
            }
        }
        finally
        {
            if (rentedBuffer is not null)
            {
                ArrayPool<int>.Shared.Return(rentedBuffer);
            }
        }
    }

    // The same world resolution MovementSystem applies to a moved body (circle by its terrain mask, box by its extent).
    private static void ResolveAgainstWorld(IWorldPhysics2D worldPhysics, ref Vector2 position, in Collider2D collider)
    {
        if (collider.Kind == ColliderKind.Circle)
        {
            _ = worldPhysics.ResolveCircleCollision(ref position, collider.Radius, collider.Mask, out _);
        }
        else
        {
            AABB2D box = collider.Box;
            _ = worldPhysics.ResolveAabbCollision(ref position, in box, out _);
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
