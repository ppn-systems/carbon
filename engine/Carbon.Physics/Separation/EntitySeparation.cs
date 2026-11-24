// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Physics.Contacts;
using Carbon.Physics.Narrowphase;
using Carbon.Physics.Shapes;

namespace Carbon.Physics.Separation;

/// <summary>
/// Provides soft and hard separation force resolution so entities do not stack or overlap on top of each other.
/// Ideal for MMORPG entity bumping, crowd separation, and path obstruction.
/// Zero heap allocation.
/// </summary>
public static class EntitySeparation
{
    /// <summary>
    /// Resolves an overlap between two circular entity colliders by separating their positions.
    /// </summary>
    /// <param name="posA">Reference to position of entity A (mutated on collision).</param>
    /// <param name="posB">Reference to position of entity B (mutated on collision).</param>
    /// <param name="radiusA">Radius of entity A collider.</param>
    /// <param name="radiusB">Radius of entity B collider.</param>
    /// <param name="separationRatio">Separation weight [0.0 = only B moves, 0.5 = equal push, 1.0 = only A moves].</param>
    /// <returns><see langword="true"/> if an overlap was resolved; otherwise, <see langword="false"/>.</returns>
    public static bool ResolveCircleOverlap(
        ref Vector2 posA,
        ref Vector2 posB,
        float radiusA,
        float radiusB,
        float separationRatio = 0.5f)
    {
        Circle2D a = new(posA, radiusA);
        Circle2D b = new(posB, radiusB);

        if (CollisionDetector.TestCircleCircle(a, b, out CollisionContact contact) && contact.HasCollision)
        {
            Vector2 displacement = contact.Normal * contact.PenetrationDepth;
            posA -= displacement * (1f - separationRatio);
            posB += displacement * separationRatio;
            return true;
        }

        return false;
    }
}
