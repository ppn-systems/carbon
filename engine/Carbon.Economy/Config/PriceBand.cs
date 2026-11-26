// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Economy.Config;

/// <summary>A price percentage that applies to buyers in a level range.</summary>
public sealed record PriceBand
{
    /// <summary>Gets or sets the first level (inclusive).</summary>
    public int MinLevel { get; set; }

    /// <summary>Gets or sets the last level (inclusive).</summary>
    public int MaxLevel { get; set; }

    /// <summary>Gets or sets the price percentage (100 = base buy price).</summary>
    public int Percent { get; set; } = 100;
}
