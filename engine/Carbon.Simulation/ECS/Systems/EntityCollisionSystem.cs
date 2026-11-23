// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Physics.Separation;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.Spatial;

namespace Carbon.Simulation.ECS.Systems;

/// <summary>
/// Resolves entity-to-entity physical collisions and prevents crowd stacking using the spatial hash grid broadphase.
/// Hot path zero heap allocation using pre-allocated stack scratch buffers.
/// </summary>
public static class EntityCollisionSystem
{
    private const int MaxQueryBuffer = 256;

    /// <summary>
    /// Synchronizes entity positions into the spatial hash grid and resolves entity-to-entity overlapping collisions.
    /// </summary>
    /// <param name="entities">The entity manager.</param>
    /// <param name="spatialGrid">The spatial partitioning grid.</param>
    public static void Update(EntityManager entities, SpatialHashGrid2D spatialGrid)
    {
        ArgumentNullException.ThrowIfNull(entities, nameof(entities));
        ArgumentNullException.ThrowIfNull(spatialGrid, nameof(spatialGrid));

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
            if (colA.IsTrigger || colA.Kind != ColliderKind.Circle)
            {
                continue;
            }

            ref Transform2D transA = ref entities.GetTransform(entityA);
            CellCoord cell = spatialGrid.WorldToCell(transA.Position);

            spatialGrid.QueryNeighbors(cell, nearbyBuffer, out int nearbyCount);

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
                if (colB.IsTrigger || colB.Kind != ColliderKind.Circle)
                {
                    continue;
                }

                // Check collision layer filtering
                if ((colA.Mask & colB.Layer) == 0 || (colB.Mask & colA.Layer) == 0)
                {
                    continue;
                }

                ref Transform2D transB = ref entities.GetTransform(entityB);

                Vector2 posA = transA.Position;
                Vector2 posB = transB.Position;

                if (EntitySeparation.ResolveCircleOverlap(ref posA, ref posB, colA.Radius, colB.Radius, separationRatio: 0.5f))
                {
                    transA.Position = posA;
                    transB.Position = posB;

                    _ = spatialGrid.Update(entityA.Id, posA);
                    _ = spatialGrid.Update(entityB.Id, posB);
                }
            }
        }
    }
}
