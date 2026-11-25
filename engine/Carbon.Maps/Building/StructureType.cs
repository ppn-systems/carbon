// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Maps.Building;

/// <summary>
/// Domain enum identifying the architectural blueprint type of a constructible structure.
/// </summary>
public enum StructureType : byte
{
    /// <summary>
    /// No structure occupying this grid tile.
    /// </summary>
    None = 0,

    /// <summary>
    /// Constructed timber wall (Medium affordance, 150 HP).
    /// </summary>
    WoodWall = 1,

    /// <summary>
    /// Constructed stone wall (Heavy affordance, 400 HP).
    /// </summary>
    StoneWall = 2,

    /// <summary>
    /// Constructed timber door (Medium affordance, 100 HP).
    /// </summary>
    WoodDoor = 3,

    /// <summary>
    /// Constructed stone door (Heavy affordance, 250 HP).
    /// </summary>
    StoneDoor = 4,

    /// <summary>
    /// Constructed timber floor planking (+15% movement speed, Light affordance).
    /// </summary>
    WoodFloor = 5,

    /// <summary>
    /// Constructed flagstone/slate floor (+20% movement speed, Light affordance).
    /// </summary>
    StoneFloor = 6,

    /// <summary>
    /// Constructed roof support column (Heavy affordance, 300 HP).
    /// </summary>
    RoofPillar = 7
}
