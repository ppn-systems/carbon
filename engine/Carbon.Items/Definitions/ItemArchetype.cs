// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Data.Abstractions;

namespace Carbon.Items.Definitions;

/// <summary>
/// Immutable static definition describing an item type's stacking, equipment slot, harvest tier, and placement behavior.
/// </summary>
/// <param name="Id">Unique numeric item definition identifier.</param>
/// <param name="Code">Stable symbolic string key (e.g. <c>iron_pickaxe</c>).</param>
/// <param name="Category">High-level item category.</param>
/// <param name="MaxStackSize">Maximum stack quantity per inventory slot.</param>
/// <param name="EquipSlot">Target equipment slot, or <see cref="EquipmentSlotKind.None"/> if not equippable.</param>
/// <param name="HarvestTier">Harvesting power tier for tools interacting with world props/structures.</param>
/// <param name="MaxDurability">Maximum durability (0 for indestructible/non-durable items).</param>
/// <param name="PlaceableStructureType">Optional <c>Carbon.Maps.Building.StructureType</c> byte when <see cref="Category"/> is <see cref="ItemCategory.Placeable"/>.</param>
/// <param name="DisplayName">Human-readable display name.</param>
/// <param name="BaseDamage">Baseline melee combat damage.</param>
/// <param name="CritChance">Critical strike probability (<c>0..1</c>).</param>
/// <param name="FloraHarvestDamage">Baseline harvest power against trees/flora.</param>
/// <param name="MineralHarvestDamage">Baseline harvest power against stone/ore/crystal.</param>
/// <param name="ArmorDefense">Flat defense contributed when equipped.</param>
/// <param name="BonusHealth">Bonus maximum health contributed when equipped.</param>
/// <param name="BonusMana">Bonus maximum mana contributed when equipped.</param>
/// <param name="HealHealth">Health restored on consumable use.</param>
/// <param name="RestoreMana">Mana restored on consumable use.</param>
/// <param name="RestoreStamina">Stamina restored on consumable use.</param>
/// <param name="ActionHint">Concise HUD tooltip hint.</param>
public sealed record ItemArchetype(
    uint Id,
    string Code,
    ItemCategory Category,
    int MaxStackSize = 99,
    EquipmentSlotKind EquipSlot = EquipmentSlotKind.None,
    byte HarvestTier = 0,
    float MaxDurability = 0f,
    byte PlaceableStructureType = 0,
    string DisplayName = "",
    float BaseDamage = 12f,
    float CritChance = 0f,
    float FloraHarvestDamage = 20f,
    float MineralHarvestDamage = 10f,
    float ArmorDefense = 0f,
    float BonusHealth = 0f,
    float BonusMana = 0f,
    float HealHealth = 0f,
    float RestoreMana = 0f,
    float RestoreStamina = 0f,
    string ActionHint = "") : IDataDefinition<uint>;
