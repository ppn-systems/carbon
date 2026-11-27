// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Economy.Trading;

/// <summary>One line of a trader basket or cart (ADR-025 D18).</summary>
/// <param name="Slot">Bag slot (sell) or stock slot (buy).</param>
/// <param name="ItemId">Item the player saw in that slot.</param>
/// <param name="Quantity">Units.</param>
public readonly record struct TradeLine(int Slot, uint ItemId, int Quantity);
