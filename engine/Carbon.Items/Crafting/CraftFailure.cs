// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Items.Crafting;

/// <summary>
/// Detailed reason a crafting operation was rejected. Values are stable (append, never renumber).
/// </summary>
public enum CraftFailure : byte
{
    /// <summary>The operation succeeded.</summary>
    None = 0,

    /// <summary>The recipe is malformed (no outputs, non-positive quantities).</summary>
    InvalidRecipe = 1,

    /// <summary>The inventory lacks one or more ingredients.</summary>
    MissingIngredients = 2,

    /// <summary>The outputs would not fit in the inventory.</summary>
    InventoryFull = 3,

    /// <summary>The crafter is not at the required station.</summary>
    WrongStation = 4,

    /// <summary>The character level is below the recipe unlock level.</summary>
    LevelTooLow = 5,

    /// <summary>The crafting skill is below the recipe requirement.</summary>
    SkillTooLow = 6,

    /// <summary>The crafter cannot pay the station fee.</summary>
    InsufficientFunds = 7
}
