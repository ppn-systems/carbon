// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Maps.Building;

/// <summary>
/// Bitmask flags representing 4-cardinal connections to adjacent walls or doors.
/// </summary>
[Flags]
public enum WallConnectivityMask : byte
{
    /// <summary>
    /// Isolated structure with no adjacent wall or door.
    /// </summary>
    None = 0,

    /// <summary>
    /// Connected to North neighbor (Y - 1).
    /// </summary>
    North = 1 << 0, // 1

    /// <summary>
    /// Connected to East neighbor (X + 1).
    /// </summary>
    East = 1 << 1, // 2

    /// <summary>
    /// Connected to South neighbor (Y + 1).
    /// </summary>
    South = 1 << 2, // 4

    /// <summary>
    /// Connected to West neighbor (X - 1).
    /// </summary>
    West = 1 << 3 // 8
}

/// <summary>
/// The 16 geometric autotiling shapes for connected walls (RimWorld / Dual-Grid 4-bit standard).
/// </summary>
public enum WallConnectionShape : byte
{
    /// <summary>
    /// Standalone pillar/column (0 connections).
    /// </summary>
    Isolated = 0,

    /// <summary>
    /// Dead-end wall cap pointing North (1 connection: N).
    /// </summary>
    EndNorth = 1,

    /// <summary>
    /// Dead-end wall cap pointing East (1 connection: E).
    /// </summary>
    EndEast = 2,

    /// <summary>
    /// L-junction corner connecting North and East (2 connections: N + E).
    /// </summary>
    CornerNorthEast = 3,

    /// <summary>
    /// Dead-end wall cap pointing South (1 connection: S).
    /// </summary>
    EndSouth = 4,

    /// <summary>
    /// Straight continuous vertical wall (2 connections: N + S).
    /// </summary>
    StraightVertical = 5,

    /// <summary>
    /// L-junction corner connecting East and South (2 connections: E + S).
    /// </summary>
    CornerEastSouth = 6,

    /// <summary>
    /// T-junction branching North, East, and South (3 connections: N + E + S).
    /// </summary>
    TJunctionNES = 7,

    /// <summary>
    /// Dead-end wall cap pointing West (1 connection: W).
    /// </summary>
    EndWest = 8,

    /// <summary>
    /// L-junction corner connecting West and North (2 connections: W + N).
    /// </summary>
    CornerWestNorth = 9,

    /// <summary>
    /// Straight continuous horizontal wall (2 connections: E + W).
    /// </summary>
    StraightHorizontal = 10,

    /// <summary>
    /// T-junction branching West, North, and East (3 connections: W + N + E).
    /// </summary>
    TJunctionWNE = 11,

    /// <summary>
    /// L-junction corner connecting South and West (2 connections: S + W).
    /// </summary>
    CornerSouthWest = 12,

    /// <summary>
    /// T-junction branching South, West, and North (3 connections: S + W + N).
    /// </summary>
    TJunctionSWN = 13,

    /// <summary>
    /// T-junction branching East, South, and West (3 connections: E + S + W).
    /// </summary>
    TJunctionESW = 14,

    /// <summary>
    /// 4-way crossroad intersection (4 connections: N + E + S + W).
    /// </summary>
    CrossIntersection = 15
}
