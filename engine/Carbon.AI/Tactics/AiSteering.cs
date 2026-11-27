// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.AI.Components;
using Carbon.Physics.Shapes;
using Carbon.Simulation.ECS;
using Carbon.Simulation.World;

namespace Carbon.AI.Tactics;

/// <summary>
/// Deterministic, allocation-free steering helpers shared by the generic AI and creature tactics: surround / flank
/// slots around a target, and wall-aware direction probing for kiting and fleeing. No trigonometry: rotations use
/// fixed sine / cosine constants so results are bit-identical across runs.
/// </summary>
public static class AiSteering
{
    private const float Cos45 = 0.70710678f;
    private const float StackedEpsilonSq = 1e-4f;

    /// <summary>
    /// Computes a surround slot on a ring of <paramref name="ringRadius"/> around <paramref name="targetPos"/>. The slot
    /// starts at the actor's own bearing from the target and is pushed away from the bearings of other AI actors that
    /// pursue the same target, so a group spreads around the target instead of stacking on one tile. Exactly stacked
    /// actors are separated by an entity-id tie-break.
    /// </summary>
    /// <param name="world">World simulation.</param>
    /// <param name="self">The steering actor.</param>
    /// <param name="selfPos">Actor position.</param>
    /// <param name="target">Shared target.</param>
    /// <param name="targetPos">Target position.</param>
    /// <param name="ringRadius">Slot ring radius (at most the melee reach).</param>
    /// <param name="scratch">Caller-owned scratch buffer for the overlap query.</param>
    /// <returns>The world-space slot point.</returns>
    public static Vector2 ComputeSurroundPoint(
        WorldSimulation world,
        Entity self,
        Vector2 selfPos,
        Entity target,
        Vector2 targetPos,
        float ringRadius,
        Span<Entity> scratch)
    {
        ArgumentNullException.ThrowIfNull(world);

        EntityManager entities = world.Entities;
        Vector2 bearing = selfPos - targetPos;
        float lenSq = bearing.LengthSquared();
        Vector2 u = lenSq > StackedEpsilonSq ? bearing / MathF.Sqrt(lenSq) : TieBreakDirection(self.Id);

        // Allies within a few ring radii compete for slots; the 90 degree spacing goal keeps up to ~4 close slots apart.
        float queryRadius = MathF.Max(ringRadius * 3f, 48f);
        world.OverlapCircle(targetPos, queryRadius, scratch, out int count);

        Vector2 push = Vector2.Zero;
        for (int i = 0; i < count; i++)
        {
            Entity other = scratch[i];
            if (other == self ||
                other == target ||
                !entities.HasTransform(other) ||
                !entities.TryGetComponent(other, out AiBrainComponent otherBrain) ||
                otherBrain.TargetEntity != target ||
                otherBrain.State is not (AiState.Chase or AiState.Attack))
            {
                continue;
            }

            Vector2 otherBearing = entities.GetTransform(other).Position - targetPos;
            float otherLenSq = otherBearing.LengthSquared();
            Vector2 v = otherLenSq > StackedEpsilonSq ? otherBearing / MathF.Sqrt(otherLenSq) : TieBreakDirection(other.Id);

            float dot = Vector2.Dot(u, v);
            if (dot <= 0f)
            {
                continue; // Already at least 90 degrees apart.
            }

            Vector2 away = u - v;
            float awayLenSq = away.LengthSquared();
            if (awayLenSq <= StackedEpsilonSq)
            {
                // Same bearing: split deterministically by id to opposite perpendiculars.
                away = self.Id < other.Id ? new Vector2(-u.Y, u.X) : new Vector2(u.Y, -u.X);
            }
            else
            {
                away /= MathF.Sqrt(awayLenSq);
            }

            push += away * dot;
        }

        Vector2 slotDir = u + push;
        float slotLenSq = slotDir.LengthSquared();
        slotDir = slotLenSq > StackedEpsilonSq ? slotDir / MathF.Sqrt(slotLenSq) : u;
        return targetPos + (slotDir * ringRadius);
    }

    /// <summary>
    /// Finds a direction close to <paramref name="preferred"/> whose path of <paramref name="probeDistance"/> is free of
    /// static obstacles, testing the preferred direction, then 45 and 90 degree rotations (the <paramref name="sideSign"/>
    /// side first). Used for kiting and fleeing so actors slide along walls instead of freezing against them.
    /// </summary>
    /// <param name="world">World simulation.</param>
    /// <param name="pos">Actor position.</param>
    /// <param name="preferred">Normalized preferred direction.</param>
    /// <param name="probeDistance">Probe length in world units.</param>
    /// <param name="sideSign">Which side to try first (positive = counter-clockwise).</param>
    /// <param name="direction">The first free normalized direction.</param>
    /// <returns><see langword="true"/> when a free direction exists.</returns>
    public static bool TryFindOpenDirection(
        WorldSimulation world,
        Vector2 pos,
        Vector2 preferred,
        float probeDistance,
        float sideSign,
        out Vector2 direction)
    {
        ArgumentNullException.ThrowIfNull(world);

        direction = Vector2.Zero;
        if (preferred == Vector2.Zero)
        {
            return false;
        }

        float s = sideSign >= 0f ? 1f : -1f;
        Vector2 perp = new(-preferred.Y * s, preferred.X * s);
        Vector2 diag = (preferred * Cos45) + (perp * Cos45);
        Vector2 diagOther = (preferred * Cos45) - (perp * Cos45);

        return Probe(world, pos, preferred, probeDistance, ref direction) ||
            Probe(world, pos, diag, probeDistance, ref direction) ||
            Probe(world, pos, diagOther, probeDistance, ref direction) ||
            Probe(world, pos, perp, probeDistance, ref direction) ||
            Probe(world, pos, -perp, probeDistance, ref direction);
    }

    private static bool Probe(WorldSimulation world, Vector2 pos, Vector2 dir, float distance, ref Vector2 result)
    {
        if (world.Raycast(new Ray2D(pos, dir, distance), out _))
        {
            return false;
        }

        result = dir;
        return true;
    }

    private static Vector2 TieBreakDirection(int id) => (id & 3) switch
    {
        0 => Vector2.UnitX,
        1 => Vector2.UnitY,
        2 => -Vector2.UnitX,
        _ => -Vector2.UnitY
    };
}
