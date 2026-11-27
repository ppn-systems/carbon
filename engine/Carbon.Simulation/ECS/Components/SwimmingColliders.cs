// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Physics.Obstacles;

namespace Carbon.Simulation.ECS.Components;

/// <summary>Builds colliders that can swim and dive (ADR-032 D10).</summary>
public static class SwimmingColliders
{
    /// <summary>
    /// Gets <paramref name="collider"/> able to swim: deep water no longer blocks it (<see cref="TerrainMask.DeepWater"/>).
    /// Players swim and dive; creatures keep the default mask, so deep water stays a wall to them.
    /// </summary>
    /// <param name="collider">The collider.</param>
    /// <returns>The swimming collider.</returns>
    public static Collider2D AsSwimmer(Collider2D collider) => collider with { Mask = collider.Mask & ~TerrainMask.DeepWater };
}
