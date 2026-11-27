// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Abstractions.Persistence;

/// <summary>Units a trader sold in one restock epoch (ADR-025), as stored in <c>trader_stock</c>.</summary>
/// <param name="TraderKey">Trader key (bit pattern of the unsigned key).</param>
/// <param name="Epoch">Restock epoch the counts belong to.</param>
/// <param name="Sold">Units sold per stock slot, little-endian <c>int32</c> each.</param>
public sealed record PersistedTraderSales(long TraderKey, long Epoch, byte[] Sold);
