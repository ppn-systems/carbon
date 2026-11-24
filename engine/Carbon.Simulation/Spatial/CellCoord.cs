// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Simulation.Spatial;

/// <summary>
/// Represents discrete 2D integer cell coordinates in a spatial hash grid.
/// </summary>
/// <param name="X">Horizontal cell index.</param>
/// <param name="Y">Vertical cell index.</param>
public readonly record struct CellCoord(int X, int Y)
{
    /// <summary>
    /// Computes a 64-bit spatial hash key uniquely representing this coordinate pair.
    /// </summary>
    public ulong Key => ((ulong)(uint)this.X << 32) | (uint)this.Y;

    /// <summary>
    /// Gets the Chebyshev distance (maximum coordinate difference) to another cell.
    /// </summary>
    /// <param name="other">The target cell coordinate.</param>
    /// <returns>The maximum coordinate offset.</returns>
    public int ChebyshevDistance(in CellCoord other) =>
        Math.Max(Math.Abs(this.X - other.X), Math.Abs(this.Y - other.Y));
}
