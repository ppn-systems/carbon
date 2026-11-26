// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Economy.Loot;

/// <summary>
/// Outcome of a loot roll. Items are written to the caller's buffer; currencies are returned here and should be
/// credited to the looter's <see cref="Currency.Wallet"/>.
/// </summary>
/// <param name="ItemCount">Number of distinct item stacks written to the output buffer.</param>
/// <param name="Gold">Gold rolled.</param>
/// <param name="Diamonds">Diamonds rolled.</param>
/// <param name="Truncated">Whether some item stacks did not fit in the output buffer.</param>
/// <param name="PityTriggered">Whether bad-luck protection forced or boosted the diamond roll that succeeded.</param>
public readonly record struct LootRollResult(int ItemCount, long Gold, int Diamonds, bool Truncated, bool PityTriggered);
