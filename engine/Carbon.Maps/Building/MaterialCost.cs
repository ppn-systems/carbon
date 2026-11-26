// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Maps.Building;

/// <summary>
/// One material requirement of a structure: an item id from the item catalog and a quantity.
/// </summary>
/// <param name="ItemId">Item id string (e.g. <c>planks</c>, <c>stone_brick</c>).</param>
/// <param name="Quantity">Units required.</param>
public readonly record struct MaterialCost(string ItemId, int Quantity);
