// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using Carbon.Contracts.Common;
using Carbon.Economy.Currency;
using Carbon.Economy.Enhancement;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Items.Enhancement;
using Carbon.Primitives.Random;
using FluentAssertions;
using Xunit;

namespace Carbon.Economy.Tests;

/// <summary>
/// One forge attempt (ADR-020): costs are spent exactly once, rejections spend nothing, outcomes are applied to the slot,
/// and the documented expected cost per level (docs/enhancement.md) matches a Monte Carlo run of the shipped table.
/// </summary>
public sealed class ForgeEnhancerTests
{
    private const int TargetSlot = 0;
    private static readonly GameEconomy s_economy = GameEconomy.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "config"));
    private static ItemRules Rules => s_economy.ItemRules;

    private static uint Ward => s_economy.Items.ResolveId("forge_ward_scroll");

    private static uint Blessed => s_economy.Items.ResolveId("blessed_forge_scroll");

    [Fact]
    public void TryEnhance_EveryOutcome_SpendsGoldMaterialsAndScrollExactlyOnce()
    {
        for (ulong seed = 1; seed <= 200; seed++)
        {
            (InventoryContainer bag, Wallet wallet) = Kit(level: 9, gold: 1_000_000);
            ItemSlot before = bag.GetSlot(TargetSlot);
            ItemEnhancementPolicy.TryGetStep(Rules, in before, out EnhancementStep? step).Should().BeTrue();
            long fee = ItemEnhancementPolicy.GetGoldFee(Rules, in before, step!);
            int[] materialsBefore = CountMaterials(bag, step!);
            int wardsBefore = bag.CountItem(Ward);

            DeterministicRandom random = new(seed);
            ForgeAttempt attempt = ForgeEnhancer.TryEnhance(Rules, wallet, bag, TargetSlot, Ward, ref random, tick: 5);

            attempt.Status.Should().Be(OpStatus.Ok);
            wallet.Gold.Should().Be(1_000_000 - fee, "the fee is charged once whatever the outcome");
            attempt.GoldSpent.Should().Be(fee);
            int[] materialsAfter = CountMaterials(bag, step!);
            for (int i = 0; i < materialsAfter.Length; i++)
            {
                (materialsBefore[i] - materialsAfter[i]).Should().Be(step!.Materials[i].Quantity, "each material is taken once");
            }

            (wardsBefore - bag.CountItem(Ward)).Should().Be(1, "one scroll per attempt");
            attempt.ProtectionConsumed.Should().BeTrue();
        }
    }

    [Theory]
    [InlineData(0L, 10, 0)]
    [InlineData(1_000_000L, 0, 0)]
    [InlineData(1_000_000L, 10, 1)]
    public void TryEnhance_MissingGoldMaterialsOrScroll_RejectsAndSpendsNothing(long gold, int materials, int scrollsMissing)
    {
        (InventoryContainer bag, Wallet wallet) = Kit(level: 6, gold: gold, materialStacks: materials);
        if (scrollsMissing > 0)
        {
            _ = bag.TryRemove(Blessed, bag.CountItem(Blessed));
        }

        ItemSlot[] snapshot = bag.Slots.ToArray();
        DeterministicRandom random = new(7UL);

        ForgeAttempt attempt = ForgeEnhancer.TryEnhance(Rules, wallet, bag, TargetSlot, Blessed, ref random);

        attempt.Status.Should().Be(OpStatus.Conflict);
        attempt.Outcome.Should().Be(EnhancementOutcome.None);
        wallet.Gold.Should().Be(gold);
        bag.Slots.ToArray().Should().Equal(snapshot, "a rejected attempt changes nothing");
    }

    [Fact]
    public void TryEnhance_ScrollOnASafeLevelOrNonScroll_IsInvalidAndSpendsNothing()
    {
        (InventoryContainer bag, Wallet wallet) = Kit(level: 2, gold: 10_000);
        ItemSlot[] snapshot = bag.Slots.ToArray();
        DeterministicRandom random = new(3UL);

        ForgeEnhancer.TryEnhance(Rules, wallet, bag, TargetSlot, Blessed, ref random).Status.Should().Be(OpStatus.Invalid, "nothing can be lost at +2");
        ForgeEnhancer.TryEnhance(Rules, wallet, bag, TargetSlot, s_economy.Items.ResolveId("iron_ore"), ref random).Status.Should().Be(OpStatus.Invalid);
        ForgeEnhancer.TryEnhance(Rules, wallet, bag, 5, 0U, ref random).Status.Should().Be(OpStatus.Invalid, "slot 5 holds materials, not gear");

        wallet.Gold.Should().Be(10_000);
        bag.Slots.ToArray().Should().Equal(snapshot);
    }

    [Fact]
    public void TryEnhance_SafeLevels_NeverDowngradeOrDestroyOverManyAttempts()
    {
        DeterministicRandom random = new(99UL);
        for (int attempt = 0; attempt < 2_000; attempt++)
        {
            byte level = (byte)(attempt % Rules.Enhancement.DowngradeMinLevel);
            (InventoryContainer bag, Wallet wallet) = Kit(level, gold: 100_000);

            ForgeAttempt result = ForgeEnhancer.TryEnhance(Rules, wallet, bag, TargetSlot, 0U, ref random);

            result.Outcome.Should().BeOneOf(EnhancementOutcome.Success, EnhancementOutcome.NoChange);
            bag.GetSlot(TargetSlot).IsEmpty.Should().BeFalse();
            bag.GetSlot(TargetSlot).UpgradeLevel.Should().BeGreaterThanOrEqualTo(level);
        }
    }

    [Fact]
    public void TryEnhance_BlessedScrollAtTopLevel_NeverLosesTheItemOrALevel()
    {
        DeterministicRandom random = new(1234UL);
        int successes = 0;
        for (int attempt = 0; attempt < 2_000; attempt++)
        {
            (InventoryContainer bag, Wallet wallet) = Kit(level: 9, gold: 100_000);
            ForgeAttempt result = ForgeEnhancer.TryEnhance(Rules, wallet, bag, TargetSlot, Blessed, ref random);
            result.Outcome.Should().BeOneOf(EnhancementOutcome.Success, EnhancementOutcome.NoChange);
            bag.GetSlot(TargetSlot).UpgradeLevel.Should().BeGreaterThanOrEqualTo(9);
            successes += result.Outcome == EnhancementOutcome.Success ? 1 : 0;
        }

        ((double)successes / 2_000).Should().BeApproximately(Rules.Enhancement.Steps[9].SuccessPermille / 1000.0, 0.03, "protection does not change the success chance");
    }

    [Fact]
    public void TryEnhance_Outcomes_AreWrittenToTheSlot()
    {
        EnhancementStep top = Rules.Enhancement.Steps[9];
        bool sawDowngrade = false;
        bool sawDestroy = false;
        bool sawSuccess = false;
        for (ulong seed = 1; seed < 400 && !(sawDowngrade && sawDestroy && sawSuccess); seed++)
        {
            (InventoryContainer bag, Wallet wallet) = Kit(level: 9, gold: 100_000);
            bag.SetSlot(TargetSlot, bag.GetSlot(TargetSlot) with { Durability = 50f });
            DeterministicRandom random = new(seed);
            ForgeAttempt result = ForgeEnhancer.TryEnhance(Rules, wallet, bag, TargetSlot, 0U, ref random);
            ItemSlot after = bag.GetSlot(TargetSlot);
            switch (result.Outcome)
            {
                case EnhancementOutcome.Success:
                    sawSuccess = true;
                    after.UpgradeLevel.Should().Be(10);
                    after.Durability.Should().BeGreaterThan(50f, "a success restores the item to its new maximum");
                    break;
                case EnhancementOutcome.NoChange:
                    after.UpgradeLevel.Should().Be(9);
                    break;
                case EnhancementOutcome.Downgrade:
                    sawDowngrade = true;
                    after.UpgradeLevel.Should().Be(8);
                    after.Durability.Should().Be(50f, "a downgrade only caps durability");
                    result.NewLevel.Should().Be(8);
                    break;
                case EnhancementOutcome.Destroyed:
                    sawDestroy = true;
                    after.IsEmpty.Should().BeTrue();
                    result.IsDestroyed.Should().BeTrue();
                    break;
                case EnhancementOutcome.None:
                default:
                    throw new InvalidOperationException($"unexpected outcome {result.Outcome}");
            }
        }

        sawSuccess.Should().BeTrue();
        sawDowngrade.Should().BeTrue();
        sawDestroy.Should().BeTrue($"+9 destroys {top.DestroyPermille / 10}% of the time");
    }

    [Fact]
    public void CreateRandom_SameInputs_RollTheSameAndANewNonceRollsDifferently()
    {
        DeterministicRandom a = ForgeEnhancer.CreateRandom(1337UL, 50UL, 9001UL, 1UL);
        DeterministicRandom b = ForgeEnhancer.CreateRandom(1337UL, 50UL, 9001UL, 1UL);
        DeterministicRandom c = ForgeEnhancer.CreateRandom(1337UL, 50UL, 9001UL, 2UL);

        ulong first = a.NextULong();
        first.Should().Be(b.NextULong(), "replaying the same attempt rolls the same outcome");
        first.Should().NotBe(c.NextULong(), "two attempts in the same tick use different streams");
    }

    /// <summary>
    /// Expected cost to take a common item from +0 to each level with the shipped table (docs/enhancement.md):
    /// without protection a destroyed item restarts at +0; with a blessed scroll on every risky attempt (+5 and up).
    /// </summary>
    [Theory]
    [InlineData(5, 5.8, 576, 0.0, 5.8, 576, 0.0)]
    [InlineData(7, 10.7, 2443, 0.0, 9.5, 2109, 3.7)]
    [InlineData(8, 15.4, 5404, 0.0, 12.0, 4095, 6.2)]
    [InlineData(9, 28.4, 14075, 0.33, 15.3, 8111, 9.5)]
    [InlineData(10, 78.4, 50358, 1.93, 20.3, 18049, 14.5)]
    public void MonteCarlo_ExpectedCostToReachLevel_MatchesTheDocumentedTable(
        int target,
        double attempts,
        double gold,
        double destroyed,
        double blessedAttempts,
        double blessedGold,
        double scrolls)
    {
        (double a, double g, double d, _) = Simulate(target, protect: false, trials: 20_000);
        (double ba, double bg, _, double s) = Simulate(target, protect: true, trials: 20_000);

        a.Should().BeApproximately(attempts, attempts * 0.06, "mean attempts");
        g.Should().BeApproximately(gold, gold * 0.06, "mean gold");
        d.Should().BeApproximately(destroyed, Math.Max(0.02, destroyed * 0.1), "mean items destroyed");
        ba.Should().BeApproximately(blessedAttempts, blessedAttempts * 0.06, "mean attempts with blessed scrolls");
        bg.Should().BeApproximately(blessedGold, blessedGold * 0.06, "mean gold with blessed scrolls");
        s.Should().BeApproximately(scrolls, Math.Max(0.05, scrolls * 0.06), "mean scrolls");
    }

    private static (double Attempts, double Gold, double Destroyed, double Scrolls) Simulate(int target, bool protect, int trials)
    {
        EnhancementTable table = Rules.Enhancement;
        table.TryGetProtection(Blessed, out EnhancementProtection blessed).Should().BeTrue();
        DeterministicRandom random = new((ulong)(target * 7919) + (protect ? 1UL : 0UL));
        long attempts = 0;
        long gold = 0;
        long destroyed = 0;
        long scrolls = 0;
        for (int t = 0; t < trials; t++)
        {
            int level = 0;
            while (level < target)
            {
                EnhancementStep step = table.Steps[level];
                bool guard = protect && blessed.AppliesTo(step);
                attempts++;
                gold += table.GetGoldFee(step, ItemRarity.Common);
                scrolls += guard ? 1 : 0;
                switch (EnhancementTable.Roll(step, ref random, guard ? blessed : null))
                {
                    case EnhancementOutcome.Success:
                        level++;
                        break;
                    case EnhancementOutcome.Downgrade:
                        level--;
                        break;
                    case EnhancementOutcome.Destroyed:
                        destroyed++;
                        level = 0;
                        break;
                    case EnhancementOutcome.None:
                    case EnhancementOutcome.NoChange:
                    default:
                        break;
                }
            }
        }

        return ((double)attempts / trials, (double)gold / trials, (double)destroyed / trials, (double)scrolls / trials);
    }

    private static (InventoryContainer Bag, Wallet Wallet) Kit(byte level, long gold, int materialStacks = 10)
    {
        InventoryContainer bag = new(PlayerInventoryLayout.TotalCapacity);
        bag.SetSlot(TargetSlot, new ItemSlot(StarterGameCatalog.IronSword, 1, Durability: 200f, UpgradeLevel: level));
        int slot = 5;
        foreach (string code in (string[])["stone_chunk", "iron_ore", "mana_crystal", "iron_ingot", "rough_gem"])
        {
            if (materialStacks > 0)
            {
                bag.SetSlot(slot++, new ItemSlot(s_economy.Items.ResolveId(code), materialStacks));
            }
        }

        bag.SetSlot(slot++, new ItemSlot(Ward, 3));
        bag.SetSlot(slot, new ItemSlot(Blessed, 3));
        Wallet wallet = new(1UL);
        if (gold > 0)
        {
            wallet.TryCredit(CurrencyKind.Gold, gold, CurrencyReason.Admin).IsOk.Should().BeTrue();
        }

        return (bag, wallet);
    }

    private static int[] CountMaterials(InventoryContainer bag, EnhancementStep step)
    {
        int[] counts = new int[step.Materials.Length];
        for (int i = 0; i < counts.Length; i++)
        {
            counts[i] = bag.CountItem(step.Materials[i].ItemId);
        }

        return counts;
    }
}
