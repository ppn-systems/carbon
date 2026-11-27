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
    RoofPillar = 7,

    /// <summary>
    /// Placeable blacksmith anvil workstation enabling field gear enhancement (+1..+10).
    /// </summary>
    ForgeAnvil = 8,

    /// <summary>
    /// Constructed iron-plated wall (Heavy affordance, 550 HP).
    /// </summary>
    MetalWall = 9,

    /// <summary>
    /// Constructed iron-plated door (Heavy affordance, 350 HP).
    /// </summary>
    MetalDoor = 10,

    /// <summary>
    /// Crafting workbench (2x1 footprint, rotatable).
    /// </summary>
    Workbench = 11,

    /// <summary>
    /// Cooking stove (requires floor support).
    /// </summary>
    Stove = 12,

    /// <summary>
    /// Open cooking campfire.
    /// </summary>
    Campfire = 13,

    /// <summary>
    /// Wooden storage chest.
    /// </summary>
    WoodChest = 14,

    /// <summary>
    /// Wooden bed (1x2 footprint, rotatable, requires floor support).
    /// </summary>
    WoodBed = 15,

    /// <summary>
    /// Cut sandstone block wall of desert settlements (Heavy affordance, 320 HP).
    /// </summary>
    SandstoneWall = 16,

    /// <summary>
    /// Wooden table (1x1 furniture).
    /// </summary>
    WoodTable = 17,

    /// <summary>
    /// Wooden storage shelf standing against a wall (1x1 furniture).
    /// </summary>
    WoodShelf = 18,

    /// <summary>
    /// Canopied market stall counter where a village merchant trades (2x1, rotatable).
    /// </summary>
    MarketStall = 19,

    /// <summary>
    /// Raised plank deck on stilts used by swamp settlements (floor, +10% movement speed).
    /// </summary>
    PlankDeck = 20
}
