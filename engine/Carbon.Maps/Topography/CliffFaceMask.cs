// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Maps.Topography;

/// <summary>
/// Bitmask flags representing cardinal vertical cliff face drops where adjacent tiles differ in height level.
/// </summary>
[Flags]
public enum CliffFaceMask : byte
{
    /// <summary>
    /// Uniform ground level with no adjacent cliff drop.
    /// </summary>
    None = 0,

    /// <summary>
    /// Vertical cliff drop facing North (Y - 1 is lower).
    /// </summary>
    North = 1 << 0, // 1

    /// <summary>
    /// Vertical cliff drop facing East (X + 1 is lower).
    /// </summary>
    East = 1 << 1, // 2

    /// <summary>
    /// Vertical cliff drop facing South (Y + 1 is lower).
    /// </summary>
    South = 1 << 2, // 4

    /// <summary>
    /// Vertical cliff drop facing West (X - 1 is lower).
    /// </summary>
    West = 1 << 3 // 8
}
