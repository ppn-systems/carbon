// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Physics.Obstacles;

/// <summary>
/// Terrain bits of a collider's collision mask: a collider whose mask holds a bit is blocked by that terrain.
/// Every collider holds them all by default; a swimmer clears <see cref="DeepWater"/> (ADR-032).
/// </summary>
public static class TerrainMask
{
    /// <summary>Deep water (Deep and Abyss tiers, <c>DeepOcean</c> tiles) blocks the collider.</summary>
    public const uint DeepWater = 1U << 30;

    /// <summary>Gets whether a collider with <paramref name="mask"/> can enter deep water.</summary>
    /// <param name="mask">The collider's collision mask.</param>
    /// <returns><c>true</c> for a swimmer.</returns>
    public static bool CanSwim(uint mask) => (mask & DeepWater) == 0;
}
