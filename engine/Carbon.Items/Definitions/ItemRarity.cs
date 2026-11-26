// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Items.Definitions;

/// <summary>
/// Loot and display rarity tier of an item. Values are stable and ascending (append, never renumber).
/// </summary>
public enum ItemRarity : byte
{
    /// <summary>Everyday drops and basic materials.</summary>
    Common = 0,

    /// <summary>Slightly rarer drops and refined goods.</summary>
    Uncommon = 1,

    /// <summary>Rare drops and advanced crafts.</summary>
    Rare = 2,

    /// <summary>Elite and boss-tier items.</summary>
    Epic = 3,

    /// <summary>Endgame chase items.</summary>
    Legendary = 4
}
