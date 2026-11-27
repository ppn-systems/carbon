// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Contracts.Common;

/// <summary>
/// Why the server refused a trader basket or cart (ADR-025 D18). The client maps each value to a localized message; on
/// any error nothing was sold or bought. Wire values are stable: append new members, never renumber.
/// </summary>
public enum TradeBatchError : byte
{
    /// <summary>The whole batch was applied.</summary>
    None = 0,

    /// <summary>The lists are empty, of different lengths, too long, or a line names a bad slot or quantity.</summary>
    InvalidRequest = 1,

    /// <summary>Two lines name the same bag or stock slot.</summary>
    DuplicateLine = 2,

    /// <summary>A bag slot no longer holds the item or the quantity the line names (the bag changed, a slot moved).</summary>
    BagChanged = 3,

    /// <summary>The trader does not buy an item in the basket.</summary>
    NotSellable = 4,

    /// <summary>The server's total differs from the one the player confirmed, or the trader restocked.</summary>
    PricesChanged = 5,

    /// <summary>Fewer units are left in a stock slot than the cart asks for.</summary>
    SoldOut = 6,

    /// <summary>The wallet cannot pay for the cart.</summary>
    NotEnoughGold = 7,

    /// <summary>The cart does not fit in the bag.</summary>
    BagFull = 8,

    /// <summary>The sale would push the wallet over its gold cap.</summary>
    WalletFull = 9,
}
