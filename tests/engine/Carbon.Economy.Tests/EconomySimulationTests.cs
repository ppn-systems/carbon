// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using Carbon.Economy.Config;
using Carbon.Economy.Currency;
using Carbon.Economy.Loot;
using Carbon.Primitives.Random;
using FluentAssertions;
using Xunit;

namespace Carbon.Economy.Tests;

/// <summary>
/// Monte-Carlo balance checks on the shipped configs. These keep gold/hour and diamond/hour inside the design bands
/// in <c>economy.json → designTargets</c>; a loot or curve edit that inflates (or starves) the economy fails CI.
/// </summary>
public sealed class EconomySimulationTests
{
    private const int GoldKills = 10_000;
    private const int DiamondKills = 1_000_000;

    public static TheoryData<string, int> MonsterLevels()
    {
        TheoryData<string, int> data = [];
        foreach (LootTable table in ShippedConfig.Loot.All)
        {
            if (table.Kind != LootSourceKind.Monster)
            {
                continue;
            }

            foreach (int level in new[] { table.MinLevel, (table.MinLevel + table.MaxLevel) / 2, table.MaxLevel }.Distinct())
            {
                data.Add(table.Id, level);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(MonsterLevels))]
    public void Simulate10kKills_GoldPerHour_StaysInsideDesignBand(string tableId, int level)
    {
        LootTable table = Table(tableId);
        DesignTargets targets = ShippedConfig.Economy.DesignTargets;
        LevelBand band = targets.GoldPerHour.Single(b => b.MinLevel <= level && level <= b.MaxLevel);
        CurrencyLedgerBuffer ledger = new();
        Wallet wallet = new(ownerId: 1, ShippedConfig.Economy.Currencies.GoldCap, ShippedConfig.Economy.Currencies.DiamondCap, ledger);
        LootContext context = new(level, PlayerLevel: level);
        LootPityState pity = default;
        Span<LootDrop> drops = stackalloc LootDrop[16];

        for (int kill = 0; kill < GoldKills; kill++)
        {
            DeterministicRandom rng = LootRoller.CreateRandom(2026, (ulong)kill, (ulong)level);
            LootRollResult result = LootRoller.Roll(table, context, ref rng, ref pity, drops);
            wallet.TryCredit(CurrencyKind.Gold, result.Gold, CurrencyReason.MonsterLoot, (ulong)kill).IsOk.Should().BeTrue();
        }

        double goldPerKill = (double)wallet.Gold / GoldKills;
        double goldPerHour = goldPerKill * targets.KillsPerHour;
        double expected = table.ExpectedGoldPerKill(context);

        ledger.GetMinted(CurrencyKind.Gold).Should().Be(wallet.Gold);
        goldPerKill.Should().BeApproximately(expected, (expected * 0.06) + 0.5, "the roller must match the analytic curve documented in docs/economy.md");
        goldPerHour.Should().BeInRange(band.Min, band.Max, $"{tableId} L{level} gold/hour must stay in the designed band");
    }

    [Theory]
    [MemberData(nameof(MonsterLevels))]
    public void Simulate1mKills_DiamondsPerHourWithPity_StaysInsideDesignBand(string tableId, int level)
    {
        LootTable table = Table(tableId);
        DesignTargets targets = ShippedConfig.Economy.DesignTargets;
        LevelBand band = targets.DiamondsPerHour.Single(b => b.MinLevel <= level && level <= b.MaxLevel);
        LootContext context = new(level, PlayerLevel: level);
        LootPityState pity = default;
        Span<LootDrop> drops = stackalloc LootDrop[16];
        DeterministicRandom rng = LootRoller.CreateRandom(77, (ulong)level, (ulong)table.Id.Length);
        long diamonds = 0;
        int longestDrought = 0;

        for (int kill = 0; kill < DiamondKills; kill++)
        {
            LootRollResult result = LootRoller.Roll(table, context, ref rng, ref pity, drops);
            diamonds += result.Diamonds;
            longestDrought = Math.Max(longestDrought, pity.KillsSinceDiamond);
        }

        double perHour = (double)diamonds / DiamondKills * targets.KillsPerHour;
        perHour.Should().BeInRange(band.Min, band.Max, $"{tableId} L{level} diamonds/hour must stay premium-rare");
        longestDrought.Should().BeLessThan(ShippedConfig.Economy.Diamond.Pity.HardCapKills, "pity caps the worst dry streak");
    }

    [Fact]
    public void EliteAndBossKills_PayMoreThanNormalKills()
    {
        LootTable orc = Table("orc");
        double normal = orc.ExpectedGoldPerKill(new LootContext(30));
        double elite = orc.ExpectedGoldPerKill(new LootContext(30, IsElite: true));
        double boss = orc.ExpectedGoldPerKill(new LootContext(30, IsBoss: true));

        elite.Should().BeGreaterThan(normal * 2);
        boss.Should().BeGreaterThan(elite * 2);
    }

    [Fact]
    public void GoldCurve_GrowsWithLevel_ButSubLinearly()
    {
        LootTable demon = Table("demon");
        long at50 = demon.GetGoldMean(50);
        long at100 = demon.GetGoldMean(100);

        at100.Should().BeGreaterThan(at50);
        at100.Should().BeLessThan(at50 * 2, "doubling level must less than double income (inflation control)");
    }

    private static LootTable Table(string id) =>
        ShippedConfig.Loot.TryGet(id, out LootTable? table) ? table : throw new KeyNotFoundException(id);
}
