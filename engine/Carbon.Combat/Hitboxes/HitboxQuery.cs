// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Primitives.Math;
using Carbon.Simulation.ECS;
using Carbon.Simulation.World;

namespace Carbon.Combat.Hitboxes;

/// <summary>
/// Zero-allocation geometric combat hitbox queries (melee cones, arcs) built atop <see cref="WorldSimulation"/>.
/// </summary>
public static class HitboxQuery
{
    private const int MaxScratchEntities = 256;

    /// <summary>
    /// Queries entities within a circular sector (arc/cone) oriented along <paramref name="forwardDir"/>.
    /// </summary>
    /// <param name="world">Target world simulation.</param>
    /// <param name="origin">Sector apex in world coordinates.</param>
    /// <param name="forwardDir">Forward facing vector (normalized internally).</param>
    /// <param name="radius">Maximum reach radius.</param>
    /// <param name="halfAngleRadians">Half-angle of the cone in radians (e.g. <c>Pi / 4</c> for a 90° cleave).</param>
    /// <param name="results">Caller-supplied span receiving matching entity handles.</param>
    /// <param name="count">Output number of entities written.</param>
    /// <param name="layerMask">Collider layer bitmask filter.</param>
    /// <param name="ignoreEntity">Optional entity handle to exclude (typically the attacker).</param>
    public static void QueryArc(
        WorldSimulation world,
        Vector2 origin,
        Vector2 forwardDir,
        float radius,
        float halfAngleRadians,
        Span<Entity> results,
        out int count,
        uint layerMask = uint.MaxValue,
        Entity ignoreEntity = default)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentOutOfRangeException.ThrowIfNegative(radius);

        count = 0;
        if (results.IsEmpty)
        {
            return;
        }

        float dirLenSq = forwardDir.LengthSquared();
        Vector2 forward = dirLenSq > 1e-6f ? Vector2.Normalize(forwardDir) : Vector2.UnitX;
        float minCos = DeterministicMath.Cos(Math.Clamp(halfAngleRadians, 0f, 3.14159265f));

        Span<Entity> candidates = stackalloc Entity[MaxScratchEntities];
        world.OverlapCircle(origin, radius, candidates, out int candidateCount, layerMask);

        for (int i = 0; i < candidateCount && count < results.Length; i++)
        {
            Entity candidate = candidates[i];
            if (candidate == ignoreEntity || !world.Entities.HasTransform(candidate))
            {
                continue;
            }

            Vector2 toTarget = world.Entities.GetTransform(candidate).Position - origin;
            float distSq = toTarget.LengthSquared();
            if (distSq <= 1e-6f)
            {
                results[count++] = candidate;
                continue;
            }

            float dot = Vector2.Dot(toTarget / MathF.Sqrt(distSq), forward);
            if (dot >= minCos)
            {
                results[count++] = candidate;
            }
        }
    }
}
