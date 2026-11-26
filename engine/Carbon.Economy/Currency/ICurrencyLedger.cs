// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Economy.Currency;

/// <summary>
/// Sink receiving every successful <see cref="Wallet"/> mutation for auditing and faucet/sink telemetry.
/// </summary>
public interface ICurrencyLedger
{
    /// <summary>
    /// Records a committed balance change.
    /// </summary>
    /// <param name="entry">The change.</param>
    void Record(in CurrencyLedgerEntry entry);
}
