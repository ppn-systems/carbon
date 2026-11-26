// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Carbon.Data.Abstractions;

namespace Carbon.Items.Definitions;

/// <summary>
/// Data-driven item definition loaded from <c>resources/config/items.json</c>.
/// Carries the economy-facing fields (rarity, tier, base price, theme tags) shared by loot, crafting and vendors.
/// Nutrition is owned by <c>food.json</c> (survival), not by this catalog.
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

    /// <summary>Gets the maximum durability (<c>0</c> for items that never wear out).</summary>
    public float MaxDurability { get; set; }

    /// <summary>Gets a value indicating whether NPC vendors sell this item to players.</summary>
    public bool VendorBuyable { get; set; }

    /// <summary>Gets the equipment slot the item goes into (<see cref="EquipmentSlotKind.None"/> when not equippable).</summary>
    public EquipmentSlotKind EquipSlot { get; set; }

    /// <summary>Gets the harvest power tier of tools.</summary>
    public byte HarvestTier { get; set; }

    /// <summary>Gets the <c>StructureType</c> byte placed by placeable kits.</summary>
    public byte PlaceableStructureType { get; set; }

    /// <summary>Gets the baseline melee combat damage.</summary>
    public float BaseDamage { get; set; } = 12f;

    /// <summary>Gets the critical strike probability (<c>0..1</c>).</summary>
    public float CritChance { get; set; }

    /// <summary>Gets the harvest power against trees and flora.</summary>
    public float FloraHarvestDamage { get; set; } = 20f;

    /// <summary>Gets the harvest power against stone, ore and crystal.</summary>
    public float MineralHarvestDamage { get; set; } = 10f;

    /// <summary>Gets the flat defense when equipped.</summary>
    public float ArmorDefense { get; set; }

    /// <summary>Gets the bonus maximum health when equipped.</summary>
    public float BonusHealth { get; set; }

    /// <summary>Gets the bonus maximum mana when equipped.</summary>
    public float BonusMana { get; set; }

    /// <summary>Gets the health restored when consumed.</summary>
    public float HealHealth { get; set; }

    /// <summary>Gets the mana restored when consumed.</summary>
    public float RestoreMana { get; set; }

    /// <summary>Gets the stamina restored when consumed.</summary>
    public float RestoreStamina { get; set; }

    /// <summary>Gets the concise HUD tooltip hint.</summary>
    public string ActionHint { get; set; } = string.Empty;

    /// <summary>Gets the melee or projectile range in world units.</summary>
    public float AttackRange { get; set; } = 72f;

    /// <summary>Gets the projectile speed (<c>0</c> = melee).</summary>
    public float ProjectileSpeed { get; set; }

    /// <summary>Gets the projectile visual archetype byte.</summary>
    public byte ProjectileArchetype { get; set; }

    /// <summary>Gets the mana cost per shot or cast.</summary>
    public float AttackManaCost { get; set; }

    /// <summary>Gets the explicit tool/weapon capability flags.</summary>
    public ToolCapabilities Capabilities { get; set; }

    /// <summary>Gets the theme tags (monster family such as <c>slime</c>, or <c>generic</c>) used by loot validation.</summary>
    public IReadOnlyList<string> Tags { get; set; } = [];
}
