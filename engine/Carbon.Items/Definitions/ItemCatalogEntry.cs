// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Carbon.Data.Abstractions;

namespace Carbon.Items.Definitions;

/// <summary>
/// Data-driven item definition loaded from <c>resources/config/items.json</c>.
/// Carries the economy-facing fields (rarity, tier, base price, nutrition) shared by loot, crafting, vendors and needs.
/// </summary>
public sealed record ItemCatalogEntry : IDataDefinition<uint>
{
    /// <summary>Gets the stable numeric item identifier (matches <see cref="ItemArchetype.Id"/> where both exist).</summary>
    public uint Id { get; set; }

    /// <summary>Gets the stable symbolic code (e.g. <c>slime_gel</c>) referenced by loot tables and recipes.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Gets the human-readable display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets the broad item category.</summary>
    public ItemCategory Category { get; set; }

    /// <summary>Gets the rarity tier.</summary>
    public ItemRarity Rarity { get; set; }

    /// <summary>Gets the progression tier (<c>0</c> = raw/basic … <c>5</c> = endgame).</summary>
    public int Tier { get; set; }

    /// <summary>Gets the maximum quantity per inventory slot.</summary>
    public int MaxStack { get; set; } = 99;

    /// <summary>Gets the reference value in gold used for vendor buy/sell, repair and fee calculations.</summary>
    public long BasePrice { get; set; }

    /// <summary>Gets the hunger restored when eaten (<c>0</c> for inedible items). Consumed by the needs system.</summary>
    public int Nutrition { get; set; }

    /// <summary>Gets the maximum durability (<c>0</c> for items that never wear out).</summary>
    public float MaxDurability { get; set; }

    /// <summary>Gets a value indicating whether NPC vendors sell this item to players.</summary>
    public bool VendorBuyable { get; set; }

    /// <summary>Gets the theme tags (monster family such as <c>slime</c>, or <c>generic</c>) used by loot validation.</summary>
    public IReadOnlyList<string> Tags { get; set; } = [];
}
