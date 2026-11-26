// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Economy.Currency;

/// <summary>
/// Server-authoritative currency balances of one character, kept outside the inventory.
/// Balances are signed 64-bit, never negative and never above their cap; every mutation is overflow-checked,
/// all-or-nothing, and recorded in the <see cref="ICurrencyLedger"/> with a gap-free <see cref="Sequence"/>.
/// Not thread-safe by design: wallets are mutated only on the world tick thread.
/// </summary>
public sealed class Wallet
{
    private readonly ICurrencyLedger? _ledger;
    private long _gold;
    private long _diamonds;

    /// <summary>
    /// Initializes a new instance of the <see cref="Wallet"/> class with zero balances.
    /// </summary>
    /// <param name="ownerId">Owning character id (written into ledger entries).</param>
    /// <param name="goldCap">Maximum gold balance.</param>
    /// <param name="diamondCap">Maximum diamond balance.</param>
    /// <param name="ledger">Optional audit ledger.</param>
    public Wallet(ulong ownerId, long goldCap = long.MaxValue, long diamondCap = long.MaxValue, ICurrencyLedger? ledger = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(goldCap);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(diamondCap);
        this.OwnerId = ownerId;
        this.GoldCap = goldCap;
        this.DiamondCap = diamondCap;
        _ledger = ledger;
    }

    /// <summary>Gets the owning character id.</summary>
    public ulong OwnerId { get; }

    /// <summary>Gets the maximum gold balance.</summary>
    public long GoldCap { get; }

    /// <summary>Gets the maximum diamond balance.</summary>
    public long DiamondCap { get; }

    /// <summary>Gets the current gold balance.</summary>
    public long Gold => _gold;

    /// <summary>Gets the current diamond balance.</summary>
    public long Diamonds => _diamonds;

    /// <summary>Gets the number of committed mutations (sequence of the last ledger entry).</summary>
    public ulong Sequence { get; private set; }

    /// <summary>
    /// Gets the balance of <paramref name="currency"/>.
    /// </summary>
    /// <param name="currency">Currency.</param>
    /// <returns>Current balance.</returns>
    public long GetBalance(CurrencyKind currency) => currency == CurrencyKind.Diamond ? _diamonds : _gold;

    /// <summary>
    /// Gets the cap of <paramref name="currency"/>.
    /// </summary>
    /// <param name="currency">Currency.</param>
    /// <returns>Maximum balance.</returns>
    public long GetCap(CurrencyKind currency) => currency == CurrencyKind.Diamond ? this.DiamondCap : this.GoldCap;

    /// <summary>
    /// Checks whether <paramref name="amount"/> can be spent.
    /// </summary>
    /// <param name="currency">Currency.</param>
    /// <param name="amount">Non-negative amount.</param>
    /// <returns><see langword="true"/> when the balance covers the amount.</returns>
    public bool CanAfford(CurrencyKind currency, long amount) => amount >= 0 && this.GetBalance(currency) >= amount;

    /// <summary>
    /// Checks whether <paramref name="amount"/> can be credited without exceeding the cap.
    /// </summary>
    /// <param name="currency">Currency.</param>
    /// <param name="amount">Non-negative amount.</param>
    /// <returns><see langword="true"/> when the credit fits.</returns>
    public bool CanReceive(CurrencyKind currency, long amount) =>
        amount >= 0 && amount <= this.GetCap(currency) - this.GetBalance(currency);

    /// <summary>
    /// Credits <paramref name="amount"/>. Rejected entirely (no partial credit) when it would exceed the cap.
    /// </summary>
    /// <param name="currency">Currency.</param>
    /// <param name="amount">Non-negative amount (<c>0</c> is a successful no-op).</param>
    /// <param name="reason">Audit reason.</param>
    /// <param name="tick">Simulation tick.</param>
    /// <param name="counterpartyId">Other party for trades.</param>
    /// <returns>The outcome.</returns>
    public EconomyResult TryCredit(CurrencyKind currency, long amount, CurrencyReason reason, ulong tick = 0, ulong counterpartyId = 0)
    {
        EconomyResult check = this.ValidateCredit(currency, amount);
        if (!check.IsOk || amount == 0)
        {
            return check;
        }

        this.Apply(currency, amount, reason, tick, counterpartyId);
        return EconomyResult.Ok;
    }

    /// <summary>
    /// Atomically checks and debits <paramref name="amount"/>. Nothing changes when funds are insufficient.
    /// </summary>
    /// <param name="currency">Currency.</param>
    /// <param name="amount">Non-negative amount (<c>0</c> is a successful no-op).</param>
    /// <param name="reason">Audit reason.</param>
    /// <param name="tick">Simulation tick.</param>
    /// <param name="counterpartyId">Other party for trades.</param>
    /// <returns>The outcome.</returns>
    public EconomyResult TrySpend(CurrencyKind currency, long amount, CurrencyReason reason, ulong tick = 0, ulong counterpartyId = 0)
    {
        EconomyResult check = this.ValidateSpend(currency, amount);
        if (!check.IsOk || amount == 0)
        {
            return check;
        }

        this.Apply(currency, -amount, reason, tick, counterpartyId);
        return EconomyResult.Ok;
    }

    /// <summary>
    /// Atomically debits gold and diamonds together: both are debited or neither is.
    /// </summary>
    /// <param name="gold">Gold amount.</param>
    /// <param name="diamonds">Diamond amount.</param>
    /// <param name="reason">Audit reason.</param>
    /// <param name="tick">Simulation tick.</param>
    /// <returns>The outcome.</returns>
    public EconomyResult TrySpend(long gold, long diamonds, CurrencyReason reason, ulong tick = 0)
    {
        EconomyResult goldCheck = this.ValidateSpend(CurrencyKind.Gold, gold);
        if (!goldCheck.IsOk)
        {
            return goldCheck;
        }

        EconomyResult diamondCheck = this.ValidateSpend(CurrencyKind.Diamond, diamonds);
        if (!diamondCheck.IsOk)
        {
            return diamondCheck;
        }

        if (gold > 0)
        {
            this.Apply(CurrencyKind.Gold, -gold, reason, tick, 0);
        }

        if (diamonds > 0)
        {
            this.Apply(CurrencyKind.Diamond, -diamonds, reason, tick, 0);
        }

        return EconomyResult.Ok;
    }

    /// <summary>
    /// Atomically moves currency between two wallets (player trade). Fails without changes when the source lacks
    /// funds or the destination would exceed its cap.
    /// </summary>
    /// <param name="from">Paying wallet.</param>
    /// <param name="to">Receiving wallet.</param>
    /// <param name="currency">Currency.</param>
    /// <param name="amount">Positive amount.</param>
    /// <param name="tick">Simulation tick.</param>
    /// <returns>The outcome.</returns>
    public static EconomyResult TryTransfer(Wallet from, Wallet to, CurrencyKind currency, long amount, ulong tick = 0)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        if (ReferenceEquals(from, to))
        {
            return EconomyResult.Fail(EconomyError.SameWallet);
        }

        if (amount <= 0)
        {
            return EconomyResult.Fail(EconomyError.InvalidAmount);
        }

        EconomyResult spend = from.ValidateSpend(currency, amount);
        if (!spend.IsOk)
        {
            return spend;
        }

        EconomyResult credit = to.ValidateCredit(currency, amount);
        if (!credit.IsOk)
        {
            return credit;
        }

        from.Apply(currency, -amount, CurrencyReason.Trade, tick, to.OwnerId);
        to.Apply(currency, amount, CurrencyReason.Trade, tick, from.OwnerId);
        return EconomyResult.Ok;
    }

    /// <summary>
    /// Restores persisted balances (character load). Not a ledger event.
    /// </summary>
    /// <param name="gold">Persisted gold.</param>
    /// <param name="diamonds">Persisted diamonds.</param>
    /// <param name="sequence">Persisted ledger sequence.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a balance is negative or above its cap.</exception>
    public void Restore(long gold, long diamonds, ulong sequence)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(gold);
        ArgumentOutOfRangeException.ThrowIfNegative(diamonds);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(gold, this.GoldCap);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(diamonds, this.DiamondCap);
        _gold = gold;
        _diamonds = diamonds;
        this.Sequence = sequence;
    }

    private EconomyResult ValidateCredit(CurrencyKind currency, long amount)
    {
        if (amount < 0 || !Enum.IsDefined(currency))
        {
            return EconomyResult.Fail(EconomyError.InvalidAmount);
        }

        return this.CanReceive(currency, amount) ? EconomyResult.Ok : EconomyResult.Fail(EconomyError.BalanceCapExceeded);
    }

    private EconomyResult ValidateSpend(CurrencyKind currency, long amount)
    {
        if (amount < 0 || !Enum.IsDefined(currency))
        {
            return EconomyResult.Fail(EconomyError.InvalidAmount);
        }

        return this.CanAfford(currency, amount) ? EconomyResult.Ok : EconomyResult.Fail(EconomyError.InsufficientFunds);
    }

    private void Apply(CurrencyKind currency, long delta, CurrencyReason reason, ulong tick, ulong counterpartyId)
    {
        long balance;
        if (currency == CurrencyKind.Diamond)
        {
            _diamonds += delta;
            balance = _diamonds;
        }
        else
        {
            _gold += delta;
            balance = _gold;
        }

        this.Sequence++;
        _ledger?.Record(new CurrencyLedgerEntry(this.OwnerId, this.Sequence, currency, delta, balance, reason, tick, counterpartyId));
    }
}
