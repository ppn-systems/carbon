// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Items.Crafting;

/// <summary>
/// Workstation a recipe must be crafted at. Values are stable (append, never renumber).
/// </summary>
public enum CraftingStation : byte
{
    /// <summary>Hand-craftable anywhere.</summary>
    None = 0,

    /// <summary>Carpentry and general assembly bench.</summary>
    Workbench = 1,

    /// <summary>Smelting furnace and anvil.</summary>
    Forge = 2,

    /// <summary>Cooking stove / campfire.</summary>
    Stove = 3,

    /// <summary>Alchemy table for potions and enchanted gear.</summary>
    Alchemy = 4
}
