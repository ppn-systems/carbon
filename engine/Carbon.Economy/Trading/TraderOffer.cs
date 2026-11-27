// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Economy.Trading;

/// <summary>One stock slot as a buyer sees it.</summary>
/// <param name="ItemId">Item id.</param>
/// <param name="Stocked">Units stocked this epoch.</param>
/// <param name="Remaining">Units still for sale this epoch.</param>
/// <param name="UnitPrice">Gold per unit for this buyer.</param>
public readonly record struct TraderOffer(uint ItemId, int Stocked, int Remaining, long UnitPrice);
