// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Items.Crafting;

/// <summary>
/// Crafting skill a recipe trains and is gated by. Values are stable (append, never renumber).
/// </summary>
public enum CraftingSkill : byte
{
    /// <summary>No skill requirement.</summary>
    None = 0,

    /// <summary>Woodworking and construction.</summary>
    Carpentry = 1,

    /// <summary>Smelting and metal gear.</summary>
    Smithing = 2,

    /// <summary>Food preparation.</summary>
    Cooking = 3,

    /// <summary>Potions and magical items.</summary>
    Alchemy = 4,

    /// <summary>Leather and cloth gear.</summary>
    Tailoring = 5
}
