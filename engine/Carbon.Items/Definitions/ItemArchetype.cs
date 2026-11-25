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
public sealed record ItemArchetype(
    uint Id,
    string Code,
    ItemCategory Category,
    int MaxStackSize = 99,
    EquipmentSlotKind EquipSlot = EquipmentSlotKind.None,
    byte HarvestTier = 0,
    float MaxDurability = 0f,
    byte PlaceableStructureType = 0) : IDataDefinition<uint>;
