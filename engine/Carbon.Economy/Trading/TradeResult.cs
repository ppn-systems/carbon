// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Contracts.Common;

namespace Carbon.Economy.Trading;

/// <summary>Outcome of a trader purchase. On failure nothing changed.</summary>
public enum TradeResult : byte
{
    /// <summary>Bought.</summary>
    Ok = 0,

    /// <summary>The trader's profile is unknown.</summary>
    UnknownTrader = 1,

    /// <summary>The stock slot or quantity is out of range.</summary>
    InvalidRequest = 2,

    /// <summary>The trader restocked since the buyer's view: prices and stock changed.</summary>
    StaleEpoch = 3,

    /// <summary>Fewer units remain than requested.</summary>
    SoldOut = 4,

    /// <summary>The buyer cannot afford it.</summary>
    InsufficientFunds = 5,

    /// <summary>The items do not fit in the buyer's bag.</summary>
    InventoryFull = 6,
}

/// <summary>Maps <see cref="TradeResult"/> to the wire status.</summary>
public static class TradeResultExtensions
{
    /// <summary>Gets the wire status of a result.</summary>
    public static OpStatus ToOpStatus(this TradeResult result) => result switch
    {
        TradeResult.Ok => OpStatus.Ok,
        TradeResult.UnknownTrader => OpStatus.NotFound,
        TradeResult.InvalidRequest => OpStatus.Invalid,
        TradeResult.StaleEpoch or TradeResult.SoldOut or TradeResult.InsufficientFunds => OpStatus.Conflict,
        TradeResult.InventoryFull => OpStatus.InventoryFull,
        _ => OpStatus.Invalid,
    };
}
