// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Economy.Loot;

/// <summary>
/// JSON shape of one loot entry.
/// </summary>
public sealed record LootEntryDefinition
{
    /// <summary>Gets the item code.</summary>
    public string Item { get; set; } = string.Empty;

    /// <summary>Gets the relative weight (weighted entries only).</summary>
    public int Weight { get; set; } = 1;

    /// <summary>Gets the minimum quantity.</summary>
    public int Min { get; set; } = 1;

    /// <summary>Gets the maximum quantity (inclusive).</summary>
    public int Max { get; set; } = 1;

    /// <summary>Gets the first monster level that can drop the entry.</summary>
    public int MinLevel { get; set; } = 1;

    /// <summary>Gets the last monster level that can drop the entry (<c>0</c> = no upper bound).</summary>
    public int MaxLevel { get; set; }
}
