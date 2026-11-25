// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Maps.Building;

/// <summary>
/// Stateless domain service computing 4-cardinal connectivity and 16 autotiling corner shapes for constructed walls.
/// Follows RimWorld dual-wall snapping standards with zero heap allocations.
/// </summary>
public static class WallAutotiler
{
    /// <summary>
    /// Computes the 4-cardinal connection mask for a structure at (tileX, tileY).
    /// </summary>
    /// <param name="tileX">Horizontal tile coordinate.</param>
    /// <param name="tileY">Vertical tile coordinate.</param>
    /// <param name="canConnect">Predicate evaluating whether the neighbor at (x, y) can connect (e.g. wall, door, pillar).</param>
    /// <returns>Cardinal bitmask flags.</returns>
    public static WallConnectivityMask ComputeConnectivity(int tileX, int tileY, Func<int, int, bool> canConnect)
    {
        ArgumentNullException.ThrowIfNull(canConnect);

        WallConnectivityMask mask = WallConnectivityMask.None;

        if (canConnect(tileX, tileY - 1))
        {
            mask |= WallConnectivityMask.North;
        }

        if (canConnect(tileX + 1, tileY))
        {
            mask |= WallConnectivityMask.East;
        }

        if (canConnect(tileX, tileY + 1))
        {
            mask |= WallConnectivityMask.South;
        }

        if (canConnect(tileX - 1, tileY))
        {
            mask |= WallConnectivityMask.West;
        }

        return mask;
    }

    /// <summary>
    /// Converts a connectivity bitmask into its corresponding 16-state autotiling shape.
    /// </summary>
    /// <param name="mask">The connectivity bitmask.</param>
    /// <returns>The geometric connection shape.</returns>
    public static WallConnectionShape ToShape(WallConnectivityMask mask) =>
        (WallConnectionShape)(byte)mask;

    /// <summary>
    /// Evaluates whether a structure category can naturally connect to adjacent walls.
    /// </summary>
    /// <param name="category">The structure category to test.</param>
    /// <returns>True if the category connects to walls (Wall, Door, Pillar); otherwise false.</returns>
    public static bool CanConnectToWall(StructureCategory category) =>
        category is StructureCategory.Wall or StructureCategory.Door or StructureCategory.Pillar;
}
