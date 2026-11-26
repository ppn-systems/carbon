// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Economy.Config;

/// <summary>
/// Wallet caps (<c>economy.json → currencies</c>).
/// </summary>
public sealed record CurrencyRules
{
    /// <summary>Gets the maximum gold balance per character.</summary>
    public long GoldCap { get; set; } = 2_000_000_000;

    /// <summary>Gets the maximum diamond balance per character.</summary>
    public long DiamondCap { get; set; } = 1_000_000;
}
