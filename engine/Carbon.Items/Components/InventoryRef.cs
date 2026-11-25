// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Items.Components;

/// <summary>
/// Links an ECS entity to an inventory container by identifier and capacity.
/// Unmanaged struct component.
/// </summary>
/// <param name="ContainerId">Identifier of the entity's backing <see cref="Containers.InventoryContainer"/>.</param>
/// <param name="Capacity">Slot capacity of the referenced container.</param>
public readonly record struct InventoryRef(int ContainerId, int Capacity);
