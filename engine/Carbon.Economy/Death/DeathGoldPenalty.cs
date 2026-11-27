// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Economy.Config;
using Carbon.Primitives.Random;

namespace Carbon.Economy.Death;

/// <summary>
/// The gold a dying player drops (ADR-031): how much (<see cref="ComputeLoss"/>) and how it is split into ground piles
/// (<see cref="SplitIntoPiles"/>). Pure and allocation free; the server debits the wallet by exactly the piles' total.
/// </summary>
public static class DeathGoldPenalty
{
    /// <summary>Upper bound on <see cref="DeathPenaltyRules.MaxPiles"/>, so callers can size a stack buffer.</summary>
    public const int MaxPilesLimit = 16;

    /// <summary>
    /// Returns the gold a player at <paramref name="level"/> carrying <paramref name="carriedGold"/> drops on death:
    /// <c>clamp(carried × dropPercent / 100, floorGoldPerLevel × level, capGoldPerLevel × level)</c>, at most the carried gold,
    /// and 0 below <see cref="DeathPenaltyRules.MinLevel"/>.
    /// </summary>
    public static long ComputeLoss(DeathPenaltyRules rules, long carriedGold, int level)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (carriedGold <= 0L || level < rules.MinLevel || rules.DropPercent <= 0)
        {
            return 0L;
        }

        long share = carriedGold * rules.DropPercent / 100L;
        long floor = (long)Math.Max(0, rules.FloorGoldPerLevel) * level;
        long cap = (long)Math.Max(0, rules.CapGoldPerLevel) * level;
        long loss = Math.Min(Math.Max(share, floor), Math.Max(floor, cap));
        return Math.Min(loss, carriedGold);
    }

    /// <summary>
    /// Splits <paramref name="amount"/> into <c>clamp(ceil(amount / goldPerPile), 1, maxPiles)</c> piles whose sizes vary
    /// by up to ±50% around an even share, drawn from <paramref name="random"/>. The piles always sum to
    /// <paramref name="amount"/> and each holds at least 1 gold.
    /// </summary>
    /// <returns>The number of piles written to <paramref name="piles"/> (0 when <paramref name="amount"/> is not positive).</returns>
    public static int SplitIntoPiles(DeathPenaltyRules rules, long amount, ref DeterministicRandom random, Span<long> piles)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (amount <= 0L || piles.IsEmpty)
        {
            return 0;
        }

        long perPile = Math.Max(1, rules.GoldPerPile);
        long wanted = (amount + perPile - 1L) / perPile;
        int count = (int)Math.Clamp(wanted, 1L, Math.Min(Math.Clamp(rules.MaxPiles, 1, MaxPilesLimit), Math.Min(piles.Length, amount)));

        Span<int> weights = stackalloc int[MaxPilesLimit];
        long weightSum = 0L;
        for (int i = 0; i < count; i++)
        {
            weights[i] = random.NextInt(50, 151);
            weightSum += weights[i];
        }

        // Every pile gets 1 gold, the rest is shared by weight; the rounding remainder goes to the last pile.
        long spread = amount - count;
        long assigned = 0L;
        for (int i = 0; i < count; i++)
        {
            long share = i == count - 1 ? spread - assigned : spread * weights[i] / weightSum;
            piles[i] = 1L + share;
            assigned += share;
        }

        return count;
    }
}
