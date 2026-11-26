// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Economy.Config;

/// <summary>
/// NPC vendor price spread (<c>economy.json → vendor</c>). Selling below buying is the main gold sink.
/// </summary>
public sealed record VendorRules
{
    /// <summary>Gets the buy price as a percent of base price.</summary>
    public int BuyMarkupPercent { get; set; } = 100;

    /// <summary>Gets the sell price as a percent of base price (must be below <see cref="BuyMarkupPercent"/>).</summary>
    public int SellRatioPercent { get; set; } = 25;
}
