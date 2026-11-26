// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Economy.Simulation;

/// <summary>
/// Per-hour gold flows of one simulated level: faucets (loot gold, vendor sales) against sinks (repairs, station fees,
/// vendor purchases), plus the diamond rate.
/// </summary>
/// <param name="Level">Monster and player level.</param>
/// <param name="MonsterGold">Gold/hour credited from monster loot.</param>
/// <param name="VendorSales">Gold/hour from selling looted items.</param>
/// <param name="Repairs">Gold/hour spent on repairs.</param>
/// <param name="CraftingFees">Gold/hour spent on station fees.</param>
/// <param name="VendorPurchases">Gold/hour spent buying from vendors.</param>
/// <param name="Diamonds">Diamonds/hour.</param>
public readonly record struct EconomyHourReport(
    int Level,
    double MonsterGold,
    double VendorSales,
    double Repairs,
    double CraftingFees,
    double VendorPurchases,
    double Diamonds)
{
    /// <summary>Gets total faucet gold/hour.</summary>
    public double Faucets => this.MonsterGold + this.VendorSales;

    /// <summary>Gets total sink gold/hour.</summary>
    public double Sinks => this.Repairs + this.CraftingFees + this.VendorPurchases;

    /// <summary>Gets net gold/hour added to the player's wallet.</summary>
    public double Net => this.Faucets - this.Sinks;

    /// <summary>Gets the share of faucet gold removed by sinks (<c>0..1</c>).</summary>
    public double SinkShare => this.Faucets <= 0d ? 0d : this.Sinks / this.Faucets;
}
