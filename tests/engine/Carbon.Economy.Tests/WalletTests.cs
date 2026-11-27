// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Contracts.Common;
using Carbon.Economy.Currency;
using Carbon.Testing;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Carbon.Economy.Tests;

public sealed class WalletTests(ITestOutputHelper output)
{
    [Fact]
    public void TryCreditAndTrySpend_AfterAFullCollection_AllocateNothing()
    {
        // ALLOC-13: the checks name the currencies instead of calling Enum.IsDefined, whose runtime metadata cache a full
        // collection can free and the next call rebuilds on the calling (tick) thread. Every gold or diamond pickup is a
        // credit.
        Wallet wallet = new(ownerId: 7);
        for (int i = 0; i < 1_000; i++)
        {
            _ = wallet.TryCredit(CurrencyKind.Gold, 2, CurrencyReason.MonsterLoot);
            _ = wallet.TrySpend(CurrencyKind.Gold, 1, CurrencyReason.VendorPurchase);
        }

        long bytes = 0;
        for (int i = 0; i < 20; i++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            AllocationWindow window = AllocationProbe.Start();
            _ = wallet.TryCredit(CurrencyKind.Diamond, 2, CurrencyReason.MonsterLoot);
            _ = wallet.TrySpend(CurrencyKind.Diamond, 1, CurrencyReason.VendorPurchase);
            bytes += window.Stop(1).Bytes;
        }

        output.WriteLine($"{bytes} B over 20 credit-and-spend pairs, each after a full collection");
        bytes.Should().Be(0, "nothing is rebuilt after a full collection");
    }

    [Fact]
    public void TryCreditAndTrySpend_EveryDefinedCurrency_IsAcceptedAndAnUndefinedOneIsNot()
    {
        // The checks list the currencies by name, so a currency added to the enum must be added to them too.
        Wallet wallet = new(ownerId: 7);
        foreach (CurrencyKind currency in Enum.GetValues<CurrencyKind>())
        {
            wallet.TryCredit(currency, 2, CurrencyReason.MonsterLoot).IsOk.Should().BeTrue($"{currency} is a currency");
            wallet.TrySpend(currency, 1, CurrencyReason.VendorPurchase).IsOk.Should().BeTrue($"{currency} is a currency");
        }

        const CurrencyKind belowTheFirst = 0;
        const CurrencyKind pastTheLast = (CurrencyKind)3;
        wallet.TryCredit(belowTheFirst, 1, CurrencyReason.MonsterLoot).Error.Should().Be(EconomyError.InvalidAmount);
        wallet.TrySpend(pastTheLast, 1, CurrencyReason.VendorPurchase).Error.Should().Be(EconomyError.InvalidAmount);
    }

    [Fact]
    public void TrySpend_WithInsufficientFunds_RejectsAndLeavesBalanceUntouched()
    {
        CurrencyLedgerBuffer ledger = new();
        Wallet wallet = new(ownerId: 7, ledger: ledger);
        wallet.TryCredit(CurrencyKind.Gold, 100, CurrencyReason.MonsterLoot).IsOk.Should().BeTrue();

        EconomyResult result = wallet.TrySpend(CurrencyKind.Gold, 101, CurrencyReason.VendorPurchase);

        result.Status.Should().Be(OpStatus.Conflict);
        result.Error.Should().Be(EconomyError.InsufficientFunds);
        wallet.Gold.Should().Be(100, "a rejected spend must not debit anything");
        wallet.Sequence.Should().Be(1UL, "only the credit was committed");
        ledger.TotalRecorded.Should().Be(1);
    }

    [Fact]
    public void TryCredit_AtLongMaxValue_RejectsOverflowWithoutWrapping()
    {
        Wallet wallet = new(ownerId: 1);
        wallet.TryCredit(CurrencyKind.Gold, long.MaxValue, CurrencyReason.Admin).IsOk.Should().BeTrue();

        EconomyResult result = wallet.TryCredit(CurrencyKind.Gold, 1, CurrencyReason.MonsterLoot);

        result.Error.Should().Be(EconomyError.BalanceCapExceeded);
        wallet.Gold.Should().Be(long.MaxValue, "credits are all-or-nothing and never wrap negative");
    }

    [Fact]
    public void TryCredit_AboveConfiguredCap_RejectsWholeAmount()
    {
        Wallet wallet = new(ownerId: 1, goldCap: 1_000, diamondCap: 10);
        wallet.TryCredit(CurrencyKind.Diamond, 9, CurrencyReason.MonsterLoot).IsOk.Should().BeTrue();

        wallet.TryCredit(CurrencyKind.Diamond, 2, CurrencyReason.MonsterLoot).Error.Should().Be(EconomyError.BalanceCapExceeded);
        wallet.Diamonds.Should().Be(9);
        wallet.TryCredit(CurrencyKind.Diamond, 1, CurrencyReason.MonsterLoot).IsOk.Should().BeTrue();
        wallet.Diamonds.Should().Be(10);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(long.MinValue)]
    public void TrySpendAndCredit_WithNegativeAmount_ReturnInvalid(long amount)
    {
        Wallet wallet = new(ownerId: 1);
        wallet.TryCredit(CurrencyKind.Gold, 50, CurrencyReason.Admin);

        wallet.TrySpend(CurrencyKind.Gold, amount, CurrencyReason.Repair).Status.Should().Be(OpStatus.Invalid);
        wallet.TryCredit(CurrencyKind.Gold, amount, CurrencyReason.Admin).Status.Should().Be(OpStatus.Invalid);
        wallet.Gold.Should().Be(50);
    }

    [Fact]
    public void TrySpend_GoldAndDiamonds_IsAllOrNothing()
    {
        Wallet wallet = new(ownerId: 1);
        wallet.TryCredit(CurrencyKind.Gold, 500, CurrencyReason.Admin);
        wallet.TryCredit(CurrencyKind.Diamond, 2, CurrencyReason.Admin);

        wallet.TrySpend(400, 3, CurrencyReason.VendorPurchase).Error.Should().Be(EconomyError.InsufficientFunds);
        wallet.Gold.Should().Be(500, "gold must not be debited when the diamond part fails");

        wallet.TrySpend(400, 2, CurrencyReason.VendorPurchase).IsOk.Should().BeTrue();
        wallet.Gold.Should().Be(100);
        wallet.Diamonds.Should().Be(0);
    }

    [Fact]
    public void TryTransfer_WhenReceiverWouldOverflowCap_ChangesNeitherWallet()
    {
        Wallet from = new(ownerId: 1);
        Wallet to = new(ownerId: 2, goldCap: 100);
        from.TryCredit(CurrencyKind.Gold, 1_000, CurrencyReason.Admin);
        to.TryCredit(CurrencyKind.Gold, 90, CurrencyReason.Admin);

        Wallet.TryTransfer(from, to, CurrencyKind.Gold, 20).Error.Should().Be(EconomyError.BalanceCapExceeded);
        from.Gold.Should().Be(1_000);
        to.Gold.Should().Be(90);

        Wallet.TryTransfer(from, to, CurrencyKind.Gold, 10).IsOk.Should().BeTrue();
        from.Gold.Should().Be(990);
        to.Gold.Should().Be(100);
        Wallet.TryTransfer(from, from, CurrencyKind.Gold, 1).Error.Should().Be(EconomyError.SameWallet);
    }

    [Fact]
    public void Ledger_RecordsGapFreeSequenceAndFaucetSinkTotals()
    {
        CurrencyLedgerBuffer ledger = new(capacity: 8);
        Wallet a = new(ownerId: 10, ledger: ledger);
        Wallet b = new(ownerId: 20, ledger: ledger);

        a.TryCredit(CurrencyKind.Gold, 300, CurrencyReason.MonsterLoot, tick: 5);
        a.TrySpend(CurrencyKind.Gold, 40, CurrencyReason.Repair, tick: 6);
        Wallet.TryTransfer(a, b, CurrencyKind.Gold, 60, tick: 7);
        a.TryCredit(CurrencyKind.Diamond, 1, CurrencyReason.MonsterLoot, tick: 8);

        ledger.GetMinted(CurrencyKind.Gold).Should().Be(300, "trades are neither faucet nor sink");
        ledger.GetBurned(CurrencyKind.Gold).Should().Be(40);
        ledger.GetMinted(CurrencyKind.Diamond).Should().Be(1);

        ledger.TryDrain(out CurrencyLedgerEntry first).Should().BeTrue();
        first.Should().Be(new CurrencyLedgerEntry(10, 1, CurrencyKind.Gold, 300, 300, CurrencyReason.MonsterLoot, 5));
        ledger.TryDrain(out CurrencyLedgerEntry second).Should().BeTrue();
        second.Sequence.Should().Be(2UL);
        second.Delta.Should().Be(-40);
        ledger.TryDrain(out CurrencyLedgerEntry tradeOut).Should().BeTrue();
        tradeOut.CounterpartyId.Should().Be(20UL);
        ledger.TryDrain(out CurrencyLedgerEntry tradeIn).Should().BeTrue();
        tradeIn.OwnerId.Should().Be(20UL);
        tradeIn.Sequence.Should().Be(1UL, "each wallet has its own sequence");
        a.Sequence.Should().Be(4UL);
    }

    [Fact]
    public void Restore_AboveCap_Throws()
    {
        Wallet wallet = new(ownerId: 1, goldCap: 10);
        System.Action act = () => wallet.Restore(11, 0, 0);
        act.Should().Throw<System.ArgumentOutOfRangeException>();
    }
}
