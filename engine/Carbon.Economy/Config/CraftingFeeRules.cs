// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Economy.Config;

/// <summary>
/// Station usage fees (<c>economy.json → craftingFees</c>). Fee = flat station fee + output value × <see cref="OutputValueRateBps"/>.
/// </summary>
public sealed record CraftingFeeRules
{
    /// <summary>Gets the flat fee for hand crafting.</summary>
    public long None { get; set; }

    /// <summary>Gets the flat fee at a workbench.</summary>
    public long Workbench { get; set; }

    /// <summary>Gets the flat fee at a forge (fuel).</summary>
    public long Forge { get; set; } = 2;

    /// <summary>Gets the flat fee at a stove (fuel).</summary>
    public long Stove { get; set; } = 1;

    /// <summary>Gets the flat fee at an alchemy table (reagents).</summary>
    public long Alchemy { get; set; } = 5;

    /// <summary>Gets the value-proportional fee in basis points of the outputs' base value (applied at stations only).</summary>
    public int OutputValueRateBps { get; set; } = 200;
}
