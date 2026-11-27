// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;

namespace Carbon.Desktop.Abstractions.World;

/// <summary>
/// Chooses the single target of the interact key. The prompt and the key both use this one function, so the prompt
/// always names what the key will do.
/// </summary>
public static class InteractionTargeting
{
    /// <summary>
    /// Picks the candidate nearest to the player among those within their own radius. Every kind is measured the same
    /// way: straight-line distance from the player's position to the target's position.
    /// </summary>
    /// <param name="player">Player position in world pixels.</param>
    /// <param name="candidates">Possible targets.</param>
    /// <returns>The nearest usable target, or <see cref="InteractionTarget.None"/>. On a tie, the earlier candidate wins.</returns>
    public static InteractionTarget SelectNearest(Vector2 player, ReadOnlySpan<InteractionCandidate> candidates)
    {
        InteractionTarget best = InteractionTarget.None;
        float bestDistanceSq = float.MaxValue;
        for (int i = 0; i < candidates.Length; i++)
        {
            ref readonly InteractionCandidate c = ref candidates[i];
            if (!c.Target.Exists)
            {
                continue;
            }

            float d2 = Vector2.DistanceSquared(player, c.Target.Position);
            if (d2 <= c.Radius * c.Radius && d2 < bestDistanceSq)
            {
                bestDistanceSq = d2;
                best = c.Target;
            }
        }

        return best;
    }
}
