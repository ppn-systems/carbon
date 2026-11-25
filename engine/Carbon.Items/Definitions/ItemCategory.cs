// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Items.Definitions;

/// <summary>
/// Broad domain classification for an <see cref="ItemArchetype"/>.
/// </summary>
public enum ItemCategory : byte
{
    /// <summary>Unspecified item category.</summary>
    None = 0,

    /// <summary>Harvested or mined raw material.</summary>
    Resource = 1,

    /// <summary>Usable consumable item.</summary>
    Consumable = 2,

    /// <summary>Harvesting or utility tool (axe, pickaxe, hammer).</summary>
    Tool = 3,

    /// <summary>Equipped combat weapon.</summary>
    Weapon = 4,

    /// <summary>Protective armor piece.</summary>
    Armor = 5,

    /// <summary>Equipped accessory (ring, amulet, charm).</summary>
    Accessory = 6,

    /// <summary>Placeable world structure blueprint item.</summary>
    Placeable = 7,

    /// <summary>Quest progression item.</summary>
    Quest = 8
}
