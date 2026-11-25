// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Maps.Transitions;

/// <summary>
/// Bitmask flags representing 8-directional neighbor adjacency for procedural autotiling,
/// Marching Squares transitions, and coastline smoothing.
/// Zero heap allocation unmanaged byte flags.
/// </summary>
[Flags]
public enum NeighborMask : byte
{
    /// <summary>
    /// No matching neighbors.
    /// </summary>
    None = 0,

    /// <summary>
    /// Matching neighbor to the North (Y - 1).
    /// </summary>
    North = 1 << 0,

    /// <summary>
    /// Matching neighbor to the East (X + 1).
    /// </summary>
    East = 1 << 1,

    /// <summary>
    /// Matching neighbor to the South (Y + 1).
    /// </summary>
    South = 1 << 2,

    /// <summary>
    /// Matching neighbor to the West (X - 1).
    /// </summary>
    West = 1 << 3,

    /// <summary>
    /// Matching diagonal neighbor to the North-East (X + 1, Y - 1).
    /// </summary>
    NorthEast = 1 << 4,

    /// <summary>
    /// Matching diagonal neighbor to the South-East (X + 1, Y + 1).
    /// </summary>
    SouthEast = 1 << 5,

    /// <summary>
    /// Matching diagonal neighbor to the South-West (X - 1, Y + 1).
    /// </summary>
    SouthWest = 1 << 6,

    /// <summary>
    /// Matching diagonal neighbor to the North-West (X - 1, Y - 1).
    /// </summary>
    NorthWest = 1 << 7
}
