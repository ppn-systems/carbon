// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Maps.Topography;

/// <summary>
/// Stateless domain service for detecting vertical cliff face drops, plateau autotiling,
/// and walkable ramp passage across discrete height levels. Zero heap allocations.
/// </summary>
public static class CliffAutotiler
{
    /// <summary>
    /// Computes the cardinal cliff drop mask for an elevated tile at (tileX, tileY).
    /// Indicates which adjacent neighbors are at a strictly lower height level.
    /// </summary>
    /// <param name="tileX">Horizontal tile coordinate.</param>
    /// <param name="tileY">Vertical tile coordinate.</param>
    /// <param name="getHeightLevel">Delegate querying discrete height level [0..2] of a tile.</param>
    /// <returns>Cliff drop bitmask flags.</returns>
    public static CliffFaceMask ComputeCliffMask(int tileX, int tileY, Func<int, int, byte> getHeightLevel)
    {
        ArgumentNullException.ThrowIfNull(getHeightLevel);

        byte currentHeight = getHeightLevel(tileX, tileY);
        if (currentHeight == 0)
        {
            return CliffFaceMask.None; // Lowlands have no downward cliffs
        }

        CliffFaceMask mask = CliffFaceMask.None;

        if (getHeightLevel(tileX, tileY - 1) < currentHeight)
        {
            mask |= CliffFaceMask.North;
        }

        if (getHeightLevel(tileX + 1, tileY) < currentHeight)
        {
            mask |= CliffFaceMask.East;
        }

        if (getHeightLevel(tileX, tileY + 1) < currentHeight)
        {
            mask |= CliffFaceMask.South;
        }

        if (getHeightLevel(tileX - 1, tileY) < currentHeight)
        {
            mask |= CliffFaceMask.West;
        }

        return mask;
    }

    /// <summary>
    /// Evaluates whether an entity can physically step across a height transition between two adjacent tiles.
    /// Movement between differing height levels requires an aligned ramp incline.
    /// </summary>
    /// <param name="fromX">Starting horizontal tile coordinate.</param>
    /// <param name="fromY">Starting vertical tile coordinate.</param>
    /// <param name="toX">Target horizontal tile coordinate.</param>
    /// <param name="toY">Target vertical tile coordinate.</param>
    /// <param name="getHeightLevel">Delegate querying discrete height level.</param>
    /// <param name="getRampDirection">Delegate querying ramp direction on a tile.</param>
    /// <returns>True if the transition is physically passable; otherwise false.</returns>
    public static bool CanStepAcross(
        int fromX,
        int fromY,
        int toX,
        int toY,
        Func<int, int, byte> getHeightLevel,
        Func<int, int, RampDirection> getRampDirection)
    {
        ArgumentNullException.ThrowIfNull(getHeightLevel);
        ArgumentNullException.ThrowIfNull(getRampDirection);

        byte fromHeight = getHeightLevel(fromX, fromY);
        byte toHeight = getHeightLevel(toX, toY);

        if (fromHeight == toHeight)
        {
            return true;
        }

        if (Math.Abs(fromHeight - toHeight) > 1)
        {
            return false; // Extreme cliff drop (>1 level) is completely impassable
        }

        // Stepping across a 1-level height difference requires an aligned ramp
        RampDirection fromRamp = getRampDirection(fromX, fromY);
        RampDirection toRamp = getRampDirection(toX, toY);

        int dx = toX - fromX;
        int dy = toY - fromY;

        // Diagonal movement across differing heights is disallowed (must climb along cardinal ramp axis)
        if (dx != 0 && dy != 0)
        {
            return false;
        }

        // Check if movement aligns with a North-South ramp
        if (dy < 0) // Moving North
        {
            return fromRamp is RampDirection.North or RampDirection.South ||
                   toRamp is RampDirection.North or RampDirection.South;
        }

        if (dy > 0) // Moving South
        {
            return fromRamp is RampDirection.North or RampDirection.South ||
                   toRamp is RampDirection.North or RampDirection.South;
        }

        // Check if movement aligns with an East-West ramp
        if (dx > 0) // Moving East
        {
            return fromRamp is RampDirection.East or RampDirection.West ||
                   toRamp is RampDirection.East or RampDirection.West;
        }

        if (dx < 0) // Moving West
        {
            return fromRamp is RampDirection.East or RampDirection.West ||
                   toRamp is RampDirection.East or RampDirection.West;
        }

        return false;
    }
}
