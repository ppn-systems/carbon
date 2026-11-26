// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Economy.Currency;

/// <summary>
/// Immutable audit record of one wallet balance change.
/// </summary>
/// <param name="OwnerId">Wallet owner (character id).</param>
/// <param name="Sequence">Per-wallet monotonically increasing mutation number (gap-free).</param>
/// <param name="Currency">Currency changed.</param>
/// <param name="Delta">Signed change (positive = credit/faucet, negative = debit/sink).</param>
/// <param name="BalanceAfter">Balance after the change.</param>
/// <param name="Reason">Audit reason.</param>
/// <param name="Tick">Simulation tick of the change.</param>
/// <param name="CounterpartyId">Other wallet owner for trades, otherwise <c>0</c>.</param>
public readonly record struct CurrencyLedgerEntry(
    ulong OwnerId,
    ulong Sequence,
    CurrencyKind Currency,
    long Delta,
    long BalanceAfter,
    CurrencyReason Reason,
    ulong Tick,
    ulong CounterpartyId = 0);
