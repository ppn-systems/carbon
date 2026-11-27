// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using Carbon.Economy.Config;
using Carbon.Economy.Death;
using Carbon.Primitives.Random;
using FluentAssertions;
using Xunit;

namespace Carbon.Economy.Tests;

/// <summary>ADR-031: the gold a dying player drops, against the shipped <c>economy.json → deathPenalty</c>.</summary>
public sealed class DeathGoldPenaltyTests
{
    private static DeathPenaltyRules Shipped => ShippedConfig.Economy.DeathPenalty;

    [Theory]
    [InlineData(9, 5_000L, 0L)] // newbie protection: below level 10 nothing drops
    [InlineData(10, 1_000L, 100L)] // 10% of carried
    [InlineData(10, 50L, 20L)] // floor 2 x level beats 10%
    [InlineData(10, 15L, 15L)] // the floor never takes more than the player carries
    [InlineData(10, 100_000L, 300L)] // cap 30 x level
    [InlineData(50, 2_000L, 200L)]
    [InlineData(50, 500L, 100L)]
    [InlineData(50, 1_000_000L, 1_500L)]
    [InlineData(100, 2_000_000_000L, 3_000L)] // a gold-capped wallet loses about one hour of net income, not the purse
    [InlineData(100, 0L, 0L)]
    public void ComputeLoss_ShippedRules_MatchesPinnedValues(int level, long carried, long expected) =>
        DeathGoldPenalty.ComputeLoss(Shipped, carried, level).Should().Be(expected, $"level {level} carrying {carried} gold");

    [Theory]
    [InlineData(100L, 1)]
    [InlineData(250L, 1)]
    [InlineData(300L, 2)]
    [InlineData(1_500L, 5)] // 6 by size, capped by maxPiles
    [InlineData(3L, 1)]
    public void SplitIntoPiles_ShippedRules_ConservesTheAmountInBoundedPiles(long amount, int expectedPiles)
    {
        DeterministicRandom random = new(2026UL);
        Span<long> piles = stackalloc long[DeathGoldPenalty.MaxPilesLimit];

        int count = DeathGoldPenalty.SplitIntoPiles(Shipped, amount, ref random, piles);

        count.Should().Be(expectedPiles);
        long sum = 0L;
        for (int i = 0; i < count; i++)
        {
            piles[i].Should().BePositive("an empty pile would be an entity worth nothing");
            sum += piles[i];
        }

        sum.Should().Be(amount, "splitting must neither create nor destroy gold");
    }

    [Fact]
    public void SplitIntoPiles_SameStream_SameSizes_OtherStream_OtherSizes()
    {
        long[] a = Split(7UL);
        long[] b = Split(7UL);
        long[] c = Split(8UL);

        a.Should().Equal(b, "the split is deterministic for a given entropy stream");
        c.Should().NotEqual(a, "pile sizes come from the stream, not a fixed even split");
    }

    [Fact]
    public void LoadFromJson_DeathPenaltyCapBelowFloor_IsRejected()
    {
        string json = ShippedConfig.Read("economy.json").Replace("\"capGoldPerLevel\": 30", "\"capGoldPerLevel\": 1", StringComparison.Ordinal);

        Action load = () => EconomyConfig.LoadFromJson(json);

        load.Should().Throw<InvalidDataException>().WithMessage("*deathPenalty*");
    }

    private static long[] Split(ulong seed)
    {
        DeterministicRandom random = new(seed);
        Span<long> piles = stackalloc long[DeathGoldPenalty.MaxPilesLimit];
        int count = DeathGoldPenalty.SplitIntoPiles(Shipped, 1_200L, ref random, piles);
        return piles[..count].ToArray();
    }
}
