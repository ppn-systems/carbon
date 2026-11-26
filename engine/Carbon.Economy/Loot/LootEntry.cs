// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Economy.Loot;

/// <summary>
/// Compiled loot entry with the item code resolved to its numeric id.
/// </summary>
/// <param name="ItemId">Item id.</param>
/// <param name="Weight">Relative weight.</param>
/// <param name="Min">Minimum quantity.</param>
/// <param name="Max">Maximum quantity (inclusive).</param>
/// <param name="MinLevel">First eligible monster level.</param>
/// <param name="MaxLevel">Last eligible monster level (<c>0</c> = unbounded).</param>
public readonly record struct LootEntry(uint ItemId, int Weight, int Min, int Max, int MinLevel, int MaxLevel)
{
    /// <summary>
    /// Checks the level band.
    /// </summary>
    /// <param name="level">Monster level.</param>
    /// <returns><see langword="true"/> when the entry can drop at <paramref name="level"/>.</returns>
    public bool AllowsLevel(int level) => level >= this.MinLevel && (this.MaxLevel == 0 || level <= this.MaxLevel);
}
