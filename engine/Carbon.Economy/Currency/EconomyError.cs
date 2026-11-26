// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Economy.Currency;

/// <summary>
/// Detailed reason an economy operation was rejected. Values are stable (append, never renumber).
/// </summary>
public enum EconomyError : byte
{
    /// <summary>The operation succeeded.</summary>
    None = 0,

    /// <summary>Negative amount, zero quantity or arithmetic overflow in the request.</summary>
    InvalidAmount = 1,

    /// <summary>The wallet balance is too low.</summary>
    InsufficientFunds = 2,

    /// <summary>The credit would push the balance past its cap.</summary>
    BalanceCapExceeded = 3,

    /// <summary>The item id is not in the catalog.</summary>
    UnknownItem = 4,

    /// <summary>Vendors do not sell this item.</summary>
    NotForSale = 5,

    /// <summary>The inventory cannot hold the items.</summary>
    InventoryFull = 6,

    /// <summary>The inventory does not hold enough of the item.</summary>
    InsufficientItems = 7,

    /// <summary>The item has no durability to repair, or the slot is empty.</summary>
    NotRepairable = 8,

    /// <summary>The item is already at full durability.</summary>
    AlreadyRepaired = 9,

    /// <summary>The craft itself was rejected; see the accompanying crafting failure.</summary>
    CraftRejected = 10,

    /// <summary>Source and destination are the same wallet.</summary>
    SameWallet = 11
}
