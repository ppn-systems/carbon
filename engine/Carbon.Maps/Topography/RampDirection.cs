// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Maps.Topography;

/// <summary>
/// Direction of an inclined natural ramp or stairs connecting two topographical height levels.
/// </summary>
public enum RampDirection : byte
{
    /// <summary>
    /// No ramp present on this tile.
    /// </summary>
    None = 0,

    /// <summary>
    /// Incline ascending toward the North (Y - 1).
    /// </summary>
    North = 1,

    /// <summary>
    /// Incline ascending toward the East (X + 1).
    /// </summary>
    East = 2,

    /// <summary>
    /// Incline ascending toward the South (Y + 1).
    /// </summary>
    South = 3,

    /// <summary>
    /// Incline ascending toward the West (X - 1).
    /// </summary>
    West = 4
}
