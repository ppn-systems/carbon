// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Maps.Props;

/// <summary>
/// Domain category classifying map environmental entities and interactive props.
/// Supports clean architectural separation (RimWorld 3-tier layer preparation).
/// </summary>
public enum PropCategory : byte
{
    /// <summary>
    /// Unclassified or ambient prop.
    /// </summary>
    None = 0,

    /// <summary>
    /// Natural vegetation (trees, bushes, mushrooms) that can be chopped or harvested for timber/fiber.
    /// </summary>
    Flora = 1,

    /// <summary>
    /// Geological rock outcrops and mineral ore deposits that can be mined for stone and metals.
    /// </summary>
    Mineral = 2,

    /// <summary>
    /// Artificial built structures, walls, fences, or ancient monuments.
    /// </summary>
    Structure = 3,

    /// <summary>
    /// Ground-level decorative scatter (wildflowers, pebbles) with zero physical collision.
    /// </summary>
    Decorative = 4
}
