// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Contracts.Common;

namespace Carbon.Economy.Currency;

/// <summary>
/// Server-authoritative outcome of an economy mutation: the wire-level <see cref="OpStatus"/> plus a detailed reason.
/// </summary>
/// <param name="Status">Canonical status sent to clients in <see cref="MutationResult"/>.</param>
/// <param name="Error">Detailed reason code (<see cref="EconomyError.None"/> on success).</param>
public readonly record struct EconomyResult(OpStatus Status, EconomyError Error)
{
    /// <summary>Gets the successful result.</summary>
    public static EconomyResult Ok => new(OpStatus.Ok, EconomyError.None);

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    public bool IsOk => this.Status == OpStatus.Ok;

    /// <summary>
    /// Creates a failed result with the canonical status for <paramref name="error"/>.
    /// </summary>
    /// <param name="error">Failure reason.</param>
    /// <returns>The failed result.</returns>
    public static EconomyResult Fail(EconomyError error) => new(ToStatus(error), error);

    private static OpStatus ToStatus(EconomyError error) => error switch
    {
        EconomyError.None => OpStatus.Ok,
        EconomyError.InvalidAmount or EconomyError.SameWallet or EconomyError.CraftRejected => OpStatus.Invalid,
        EconomyError.InsufficientFunds or EconomyError.BalanceCapExceeded or EconomyError.InsufficientItems => OpStatus.Conflict,
        EconomyError.UnknownItem => OpStatus.NotFound,
        EconomyError.NotForSale => OpStatus.Forbidden,
        EconomyError.InventoryFull => OpStatus.InventoryFull,
        _ => OpStatus.Invalid
    };
}
