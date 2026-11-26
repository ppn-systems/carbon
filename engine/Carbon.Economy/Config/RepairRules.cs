// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Economy.Config;

/// <summary>
/// Repair sink (<c>economy.json → repair</c>). Cost = <c>basePrice × CostPercentOfBasePrice% × missing/max durability</c>.
/// </summary>
public sealed record RepairRules
{
    /// <summary>Gets the cost of a full repair (0 → max durability) as a percent of base price.</summary>
    public int CostPercentOfBasePrice { get; set; } = 30;

    /// <summary>Gets the minimum charge for any non-zero repair.</summary>
    public long MinimumCost { get; set; } = 1;
}
