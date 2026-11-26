// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Items.Definitions;

namespace Carbon.Economy.Config;

/// <summary>
/// A percentage per <see cref="ItemRarity"/> (100 = unchanged), used to scale repair costs and station fees by item tier.
/// </summary>
public sealed record RarityPercents
{
    /// <summary>Gets or sets the percentage for common items.</summary>
    public int Common { get; set; } = 100;

    /// <summary>Gets or sets the percentage for uncommon items.</summary>
    public int Uncommon { get; set; } = 100;

    /// <summary>Gets or sets the percentage for rare items.</summary>
    public int Rare { get; set; } = 100;

    /// <summary>Gets or sets the percentage for epic items.</summary>
    public int Epic { get; set; } = 100;

    /// <summary>Gets or sets the percentage for legendary items.</summary>
    public int Legendary { get; set; } = 100;

    /// <summary>Gets whether every percentage is non-negative.</summary>
    public bool IsValid => this.Common >= 0 && this.Uncommon >= 0 && this.Rare >= 0 && this.Epic >= 0 && this.Legendary >= 0;

    /// <summary>Returns the percentage for <paramref name="rarity"/>.</summary>
    /// <param name="rarity">Item rarity.</param>
    /// <returns>The percentage (100 for an unknown rarity).</returns>
    public int For(ItemRarity rarity) => rarity switch
    {
        ItemRarity.Common => this.Common,
        ItemRarity.Uncommon => this.Uncommon,
        ItemRarity.Rare => this.Rare,
        ItemRarity.Epic => this.Epic,
        ItemRarity.Legendary => this.Legendary,
        _ => 100,
    };
}
