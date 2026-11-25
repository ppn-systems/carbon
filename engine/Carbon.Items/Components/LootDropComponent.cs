// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Items.Containers;

namespace Carbon.Items.Components;

/// <summary>
/// Marks a world entity as a pickable ground loot drop carrying an <see cref="ItemSlot"/>.
/// Unmanaged struct component.
/// </summary>
/// <param name="Item">The dropped item stack.</param>
/// <param name="OwnerEntityId">Optional entity slot ID that has temporary loot priority (0 for free-for-all).</param>
/// <param name="DespawnAtTick">Simulation tick index after which the loot drop expires (0 for permanent).</param>
public readonly record struct LootDropComponent(ItemSlot Item, int OwnerEntityId = 0, ulong DespawnAtTick = 0);
