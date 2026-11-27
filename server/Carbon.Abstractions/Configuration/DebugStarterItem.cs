// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Abstractions.Configuration;

/// <summary>An extra item granted to new characters by the Debug-only <c>CARBON_DEBUG_STARTER_ITEMS</c> override.</summary>
/// <param name="ItemId">Item id.</param>
/// <param name="Quantity">Quantity (at least 1).</param>
/// <param name="UpgradeLevel">Enhancement level (<c>0..10</c>).</param>
public readonly record struct DebugStarterItem(uint ItemId, int Quantity, byte UpgradeLevel);
