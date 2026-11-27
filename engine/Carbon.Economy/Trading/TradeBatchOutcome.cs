// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Contracts.Common;

namespace Carbon.Economy.Trading;

/// <summary>Outcome of a whole basket or cart (ADR-025 D18). On any error nothing changed.</summary>
/// <param name="Error">Why it was refused, or <see cref="TradeBatchError.None"/>.</param>
/// <param name="Line">Index of the first failing line, or -1.</param>
/// <param name="Gold">Gold paid (sell) or spent (buy) when it succeeded.</param>
public readonly record struct TradeBatchOutcome(TradeBatchError Error, int Line, long Gold)
{
    /// <summary>Gets whether every line was applied.</summary>
    public bool IsOk => this.Error == TradeBatchError.None;

    /// <summary>A success.</summary>
    public static TradeBatchOutcome Ok(long gold) => new(TradeBatchError.None, -1, gold);

    /// <summary>A refusal blamed on <paramref name="line"/> (-1: the batch as a whole).</summary>
    public static TradeBatchOutcome Fail(TradeBatchError error, int line = -1) => new(error, line, 0);

    /// <summary>Maps a batch error to the wire status.</summary>
    public static OpStatus ToOpStatus(TradeBatchError error) => error switch
    {
        TradeBatchError.None => OpStatus.Ok,
        TradeBatchError.InvalidRequest or TradeBatchError.DuplicateLine or TradeBatchError.NotSellable => OpStatus.Invalid,
        TradeBatchError.BagChanged or TradeBatchError.PricesChanged or TradeBatchError.SoldOut or
        TradeBatchError.NotEnoughGold or TradeBatchError.WalletFull => OpStatus.Conflict,
        TradeBatchError.BagFull => OpStatus.InventoryFull,
        _ => OpStatus.Invalid,
    };
}
