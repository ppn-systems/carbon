// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Economy.Trading;

/// <summary>One item a trader profile may stock.</summary>
/// <param name="ItemId">Item id (resolved from the <c>items.json</c> code).</param>
/// <param name="Weight">Relative chance of being picked for a stock slot.</param>
/// <param name="MinQuantity">Least units stocked when picked.</param>
/// <param name="MaxQuantity">Most units stocked when picked.</param>
/// <param name="StyleMask">Village styles that stock it (bit per <see cref="TraderCatalog.StyleNames"/> index); 0 = all.</param>
public readonly record struct TraderStockEntry(uint ItemId, int Weight, int MinQuantity, int MaxQuantity, byte StyleMask)
{
    /// <summary>Gets whether a village of <paramref name="style"/> stocks this entry.</summary>
    public bool AppliesTo(byte style) => this.StyleMask == 0 || (style < 8 && (this.StyleMask & (1 << style)) != 0);
}
