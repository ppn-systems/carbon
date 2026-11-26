// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Maps.Building;

/// <summary>
/// Categorizes the physical role of a constructed structure within the 3-layer architecture.
/// </summary>
public enum StructureCategory : byte
{
    /// <summary>
    /// Unspecified or empty structure.
    /// </summary>
    None = 0,

    /// <summary>
    /// Solid vertical barrier on Layer 2 that blocks movement, line of sight, and supports overhead roofs.
    /// </summary>
    Wall = 1,

    /// <summary>
    /// Interactive passage on Layer 2 that connects walls and can be toggled open or closed.
    /// </summary>
    Door = 2,

    /// <summary>
    /// Ground-level flooring on Layer 1 that provides cleanliness and movement speed modifiers.
    /// </summary>
    Floor = 3,

    /// <summary>
    /// Vertical support column on Layer 2 that holds up roofs within a 6-tile radius without enclosing the room.
    /// </summary>
    Pillar = 4,

    /// <summary>
    /// Interactive crafting or blacksmith workstation placed on Layer 2.
    /// </summary>
    Station = 5,

    /// <summary>
    /// Solid furniture on Layer 2 (chests, beds, tables) that blocks movement but does not connect to walls.
    /// </summary>
    Furniture = 6
}
