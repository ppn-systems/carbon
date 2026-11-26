// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;

namespace Carbon.Economy.Config;

/// <summary>
/// Behaviour of an engaged player used by the economy balance simulation (<c>economy.json → playerModel</c>).
/// Not used at runtime; it turns the faucet and sink tables into gold/hour figures CI can check.
/// </summary>
public sealed record PlayerModel
{
    /// <summary>Gets the percent of looted item units sold to vendors (the rest is used for crafting).</summary>
    public int SellLootPercent { get; set; } = 50;

    /// <summary>Gets the durability percent at which the weapon is repaired.</summary>
    public int RepairBelowPercent { get; set; } = 50;

    /// <summary>Gets the station crafts per hour (each pays its station fee).</summary>
    public int CraftsPerHour { get; set; } = 4;

    /// <summary>Gets the weapon, crafting and consumable stages by level.</summary>
    public IReadOnlyList<PlayerStage> Stages { get; set; } = [];
}
