// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Economy.Currency;

/// <summary>
/// Wallet currency. Values are stable (append, never renumber).
/// </summary>
public enum CurrencyKind : byte
{
    /// <summary>Common soft currency dropped by monsters and earned from vendors.</summary>
    Gold = 1,

    /// <summary>Premium-rare hard currency from elite/boss/deep monsters and rich ore veins.</summary>
    Diamond = 2
}
