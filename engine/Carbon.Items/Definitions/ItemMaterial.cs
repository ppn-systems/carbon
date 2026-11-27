// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Items.Definitions;

/// <summary>
/// What a tool or weapon is made of (<c>items.json</c> <c>material</c>). The material marks the progression tier the item
/// belongs to: wood is made by hand, stone at a workbench.
/// </summary>
public enum ItemMaterial : byte
{
    /// <summary>Not stated.</summary>
    None = 0,

    /// <summary>Wood, plant fiber and rope; hand-crafted.</summary>
    Wood = 1,

    /// <summary>Stone heads on wooden hafts; crafted at a workbench.</summary>
    Stone = 2,
}
