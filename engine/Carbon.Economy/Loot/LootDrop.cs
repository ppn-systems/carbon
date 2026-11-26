// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Economy.Loot;

/// <summary>
/// One rolled item stack.
/// </summary>
/// <param name="ItemId">Item id.</param>
/// <param name="Quantity">Quantity.</param>
public readonly record struct LootDrop(uint ItemId, int Quantity);
