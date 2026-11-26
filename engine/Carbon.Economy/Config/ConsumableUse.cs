// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Economy.Config;

/// <summary>A consumable the modeled player buys from a vendor every hour.</summary>
public sealed record ConsumableUse
{
    /// <summary>Gets or sets the item code (must be vendor-buyable).</summary>
    public string Item { get; set; } = string.Empty;

    /// <summary>Gets or sets the units bought per hour.</summary>
    public int PerHour { get; set; }
}
