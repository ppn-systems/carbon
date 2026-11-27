// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Economy.Loot;
using Carbon.Items.Definitions;
using Carbon.Primitives.Random;
using Carbon.Testing;
using FluentAssertions;
using Xunit;

namespace Carbon.Economy.Tests;

public sealed class LootRollerTests
{
    private static LootTable Table(string id) =>
        ShippedConfig.Loot.TryGet(id, out LootTable? table) ? table : throw new KeyNotFoundException(id);

    private static uint Id(string code) => ShippedConfig.Items.ResolveId(code);

    [Fact]
    public void Roll_WithSameSeed_IsDeterministic()
    {
        LootTable demon = Table("demon");
        LootContext context = new(SourceLevel: 70, PlayerLevel: 70, IsElite: true, DistanceTier: 3);
        Span<LootDrop> a = stackalloc LootDrop[16];
        Span<LootDrop> b = stackalloc LootDrop[16];

        for (ulong kill = 0; kill < 500; kill++)
        {
            DeterministicRandom rngA = LootRoller.CreateRandom(TestGameplayKeys.Key(42), tick: kill, sourceId: 9_000 + kill);
            DeterministicRandom rngB = LootRoller.CreateRandom(TestGameplayKeys.Key(42), tick: kill, sourceId: 9_000 + kill);
            LootPityState pityA = new(123);
            LootPityState pityB = new(123);

            LootRollResult ra = LootRoller.Roll(demon, context, ref rngA, ref pityA, a);
            LootRollResult rb = LootRoller.Roll(demon, context, ref rngB, ref pityB, b);

            rb.Should().Be(ra);
            pityB.Should().Be(pityA);
            b[..rb.ItemCount].ToArray().Should().Equal(a[..ra.ItemCount].ToArray());
        }
    }

    [Fact]
    public void Roll_Slime_OnlyDropsSlimeThemedOrGenericItems()
    {
        LootTable slime = Table("slime");
        HashSet<uint> seen = [];
        Span<LootDrop> drops = stackalloc LootDrop[16];
        LootPityState pity = default;

        for (int kill = 0; kill < 10_000; kill++)
        {
            DeterministicRandom rng = LootRoller.CreateRandom(TestGameplayKeys.Key(7), (ulong)kill, 1);
            LootContext context = new(SourceLevel: 1 + (kill % 15), IsElite: kill % 10 == 0, IsBoss: kill % 97 == 0);
            LootRollResult result = LootRoller.Roll(slime, context, ref rng, ref pity, drops);
            for (int i = 0; i < result.ItemCount; i++)
            {
                seen.Add(drops[i].ItemId);
            }
        }

        foreach (uint itemId in seen)
        {
            ShippedConfig.Items.TryGet(itemId, out ItemCatalogEntry? item).Should().BeTrue();
            item!.Tags.Should().Contain(t => t == "slime" || t == "generic", $"slime dropped '{item.Code}'");
        }

        seen.Should().Contain(Id("slime_gel")).And.Contain(Id("slime_core"));
        seen.Should().NotContain(Id("orc_tusk")).And.NotContain(Id("demon_horn")).And.NotContain(Id("bone"));
    }

    [Fact]
    public void Roll_BelowEntryMinLevel_NeverDropsGatedItems()
    {
        LootTable slime = Table("slime");
        uint core = Id("slime_core");
        uint gem = Id("rough_gem");
        Span<LootDrop> drops = stackalloc LootDrop[16];
        LootPityState pity = default;
        bool coreAtLevel5 = false;

        for (int kill = 0; kill < 20_000; kill++)
        {
            int level = 1 + (kill % 9);
            DeterministicRandom rng = LootRoller.CreateRandom(TestGameplayKeys.Key(11), (ulong)kill, 2);
            LootRollResult result = LootRoller.Roll(slime, new LootContext(level, IsBoss: true), ref rng, ref pity, drops);
            for (int i = 0; i < result.ItemCount; i++)
            {
                drops[i].ItemId.Should().NotBe(gem, "rough_gem unlocks at slime level 10");
                if (drops[i].ItemId == core)
                {
                    level.Should().BeGreaterThanOrEqualTo(5, "slime_core unlocks at level 5");
                    coreAtLevel5 = true;
                }
            }
        }

        coreAtLevel5.Should().BeTrue("slime_core should drop once its level band opens");
    }

    [Fact]
    public void Roll_Always_IncludesGuaranteedEntries()
    {
        LootTable skeleton = Table("skeleton");
        uint bone = Id("bone");
        Span<LootDrop> drops = stackalloc LootDrop[16];
        LootPityState pity = default;

        for (int kill = 0; kill < 1_000; kill++)
        {
            DeterministicRandom rng = LootRoller.CreateRandom(TestGameplayKeys.Key(3), (ulong)kill, 5);
            LootRollResult result = LootRoller.Roll(skeleton, new LootContext(20), ref rng, ref pity, drops);
            drops[..result.ItemCount].ToArray().Should().Contain(d => d.ItemId == bone && d.Quantity >= 1);
        }
    }

    [Fact]
    public void Roll_WithTinyBuffer_ReportsTruncationInsteadOfOverrunning()
    {
        LootTable orc = Table("orc");
        Span<LootDrop> drops = stackalloc LootDrop[1];
        LootPityState pity = default;
        bool truncated = false;

        for (int kill = 0; kill < 200 && !truncated; kill++)
        {
            DeterministicRandom rng = LootRoller.CreateRandom(TestGameplayKeys.Key(5), (ulong)kill, 9);
            LootRollResult result = LootRoller.Roll(orc, new LootContext(30, IsBoss: true), ref rng, ref pity, drops);
            result.ItemCount.Should().BeLessThanOrEqualTo(1);
            truncated |= result.Truncated;
        }

        truncated.Should().BeTrue();
        orc.MaxDrops(new LootContext(30, IsBoss: true)).Should().Be(1 + 2 + 3, "guaranteed + rolls + boss extra rolls");
    }

    [Fact]
    public void Roll_SteadyState_AllocatesNothing()
    {
        LootTable demon = Table("demon");
        LootPityState pity = default;
        LootContext context = new(80, PlayerLevel: 80, IsElite: true, DistanceTier: 4);
        DeterministicRandom rng = LootRoller.CreateRandom(TestGameplayKeys.Key(1), 1, 1);

        AllocationResult window = AllocationProbe.Measure(10_000, warmupIterations: 10_000, kill =>
        {
            Span<LootDrop> drops = stackalloc LootDrop[16];
            _ = LootRoller.Roll(demon, context, ref rng, ref pity, drops);
        });

        window.Bytes.Should().Be(0, $"the roll path is on the kill hot path ({window})");
    }

    [Fact]
    public void Roll_AtPityHardCap_GuaranteesDiamondAndResets()
    {
        LootTable slime = Table("slime");
        int hardCap = ShippedConfig.Economy.Diamond.Pity.HardCapKills;
        LootPityState pity = new(hardCap - 1);
        DeterministicRandom rng = LootRoller.CreateRandom(TestGameplayKeys.Key(9), 9, 9);
        Span<LootDrop> drops = stackalloc LootDrop[16];

        LootRollResult result = LootRoller.Roll(slime, new LootContext(1), ref rng, ref pity, drops);

        result.Diamonds.Should().Be(1);
        result.PityTriggered.Should().BeTrue();
        pity.KillsSinceDiamond.Should().Be(0);
    }

    [Fact]
    public void Roll_TrivialMonster_GivesNoDiamondsNoPityAndReducedGold()
    {
        LootTable slime = Table("slime");
        LootPityState pity = new(ShippedConfig.Economy.Diamond.Pity.HardCapKills - 1);
        Span<LootDrop> drops = stackalloc LootDrop[16];
        long trivialGold = 0;
        long fairGold = 0;

        for (int kill = 0; kill < 2_000; kill++)
        {
            DeterministicRandom rng = LootRoller.CreateRandom(TestGameplayKeys.Key(4), (ulong)kill, 4);
            LootRollResult trivial = LootRoller.Roll(slime, new LootContext(15, PlayerLevel: 60), ref rng, ref pity, drops);
            trivial.Diamonds.Should().Be(0, "gray monsters never drop diamonds, even at the pity cap");
            trivialGold += trivial.Gold;

            DeterministicRandom rng2 = LootRoller.CreateRandom(TestGameplayKeys.Key(4), (ulong)kill, 4);
            LootPityState fresh = default;
            fairGold += LootRoller.Roll(slime, new LootContext(15, PlayerLevel: 15), ref rng2, ref fresh, drops).Gold;
        }

        pity.KillsSinceDiamond.Should().Be(ShippedConfig.Economy.Diamond.Pity.HardCapKills - 1, "trivial kills do not advance pity");
        trivialGold.Should().BeLessThan(fairGold / 2);
    }

    [Fact]
    public void DiamondChance_EliteBossAndDistance_ScaleUp()
    {
        LootTable demon = Table("demon");
        long normal = demon.DiamondChancePpm(new LootContext(80));
        long elite = demon.DiamondChancePpm(new LootContext(80, IsElite: true));
        long boss = demon.DiamondChancePpm(new LootContext(80, IsBoss: true));
        long deep = demon.DiamondChancePpm(new LootContext(80, DistanceTier: 8));

        normal.Should().BePositive();
        elite.Should().Be(normal * ShippedConfig.Economy.Diamond.EliteMultiplier);
        boss.Should().BeGreaterThan(elite);
        deep.Should().BeGreaterThan(normal);
        demon.DiamondChancePpm(new LootContext(100)).Should().BeGreaterThan(demon.DiamondChancePpm(new LootContext(45)), "deeper levels pay more");
    }
}
