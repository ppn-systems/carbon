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

    /// <summary>Gets the durability each equipped piece loses per kill.</summary>
    public float DurabilityLossPerKill { get; set; } = 0.5f;

    /// <summary>Gets the durability percent at which gear is repaired.</summary>
    public int RepairBelowPercent { get; set; } = 50;

    /// <summary>Gets the station crafts per hour (each pays its station fee).</summary>
    public int CraftsPerHour { get; set; } = 4;

    /// <summary>Gets the vendor food purchases per hour.</summary>
    public int FoodBoughtPerHour { get; set; } = 6;

    /// <summary>Gets the item code of the food bought from vendors.</summary>
    public string FoodItem { get; set; } = "raw_meat";

    /// <summary>Gets the gear and crafting stages by level.</summary>
    public IReadOnlyList<PlayerStage> Stages { get; set; } = [];
}
