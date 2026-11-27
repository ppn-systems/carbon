// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Text.Json;
using Carbon.Contracts.Common;
using Carbon.Items.Catalog;
using Carbon.Items.Definitions;
using Carbon.Items.Enhancement;
using Carbon.Items.Serialization;
using Carbon.Primitives.Random;
using FluentAssertions;
using Xunit;

namespace Carbon.Items.Tests;

/// <summary>The shipped <c>enhancement.json</c> (ADR-020): validation, safe levels, roll distribution and protection.</summary>
public sealed class EnhancementTableTests
{
    private static readonly ItemRules s_rules = ItemRules.LoadShippedDefaults();
    private static readonly string s_shippedJson = ItemRules.ReadShippedDocument("enhancement.json");

    private static EnhancementTable Table => s_rules.Enhancement;

    [Fact]
    public void LoadShippedDefaults_EnhancementJson_CoversEveryLevelUpToTheMax()
    {
        Table.MaxLevel.Should().Be(ItemEnhancementPolicy.MaxUpgradeLevel);
        for (int i = 0; i < Table.Steps.Length; i++)
        {
            EnhancementStep step = Table.Steps[i];
            step.FromLevel.Should().Be((byte)i);
            (step.SuccessPermille + step.NoChangePermille + step.DowngradePermille + step.DestroyPermille)
                .Should().Be(EnhancementTable.WeightTotal, $"level {i} weights are a full distribution");
            step.MaterialSpan.Length.Should().BeGreaterThan(0, "every attempt is a material sink");
            step.BaseGold.Should().BeGreaterThan(0, "every attempt is a gold sink");
        }
    }

    [Fact]
    public void ShippedTable_SafeLevels_NeverDowngradeOrDestroy()
    {
        Table.DowngradeMinLevel.Should().Be(5, "classic safe zone: +0..+4 attempts cannot lose a level");
        Table.DestroyMinLevel.Should().Be(8, "items can only break from +8");
        for (int i = 0; i < Table.Steps.Length; i++)
        {
            EnhancementStep step = Table.Steps[i];
            if (i < Table.DowngradeMinLevel)
            {
                step.DowngradePermille.Should().Be(0, $"+{i} is a safe level");
            }

            if (i < Table.DestroyMinLevel)
            {
                step.DestroyPermille.Should().Be(0, $"+{i} cannot destroy");
            }
        }

        for (int roll = 0; roll < EnhancementTable.WeightTotal; roll++)
        {
            for (int i = 0; i < Table.DowngradeMinLevel; i++)
            {
                EnhancementTable.Resolve(Table.Steps[i], roll).Should().BeOneOf(EnhancementOutcome.Success, EnhancementOutcome.NoChange);
            }
        }
    }

    [Fact]
    public void ShippedTable_GoldFee_ScalesWithLevelAndRarity()
    {
        for (int i = 1; i < Table.Steps.Length; i++)
        {
            Table.Steps[i].BaseGold.Should().BeGreaterThan(Table.Steps[i - 1].BaseGold, "fees rise with level");
            Table.Steps[i].SuccessPermille.Should().BeLessThanOrEqualTo(Table.Steps[i - 1].SuccessPermille, "chances never rise with level");
        }

        EnhancementStep top = Table.Steps[^1];
        Table.GetGoldFee(top, ItemRarity.Common).Should().Be(top.BaseGold);
        Table.GetGoldFee(top, ItemRarity.Legendary).Should().BeGreaterThan(Table.GetGoldFee(top, ItemRarity.Epic));
        Table.GetGoldFee(top, ItemRarity.Epic).Should().BeGreaterThan(Table.GetGoldFee(top, ItemRarity.Rare));
        Table.GetGoldFee(top, ItemRarity.Rare).Should().BeGreaterThan(Table.GetGoldFee(top, ItemRarity.Uncommon));
        Table.GetGoldFee(top, ItemRarity.Uncommon).Should().BeGreaterThan(Table.GetGoldFee(top, ItemRarity.Common));
    }

    [Fact]
    public void Roll_ManyAttemptsPerLevel_MatchesTheConfiguredDistribution()
    {
        const int trials = 100_000;
        const double tolerance = 0.006;
        DeterministicRandom random = new(0xC0FFEEUL);
        Span<int> counts = stackalloc int[5];
        for (int i = 0; i < Table.Steps.Length; i++)
        {
            EnhancementStep step = Table.Steps[i];
            counts.Clear();
            for (int t = 0; t < trials; t++)
            {
                counts[(int)EnhancementTable.Roll(step, ref random)]++;
            }

            ((double)counts[(int)EnhancementOutcome.Success] / trials).Should().BeApproximately(step.SuccessPermille / 1000.0, tolerance, $"+{i} success");
            ((double)counts[(int)EnhancementOutcome.NoChange] / trials).Should().BeApproximately(step.NoChangePermille / 1000.0, tolerance, $"+{i} no change");
            ((double)counts[(int)EnhancementOutcome.Downgrade] / trials).Should().BeApproximately(step.DowngradePermille / 1000.0, tolerance, $"+{i} downgrade");
            ((double)counts[(int)EnhancementOutcome.Destroyed] / trials).Should().BeApproximately(step.DestroyPermille / 1000.0, tolerance, $"+{i} destroy");
            counts[(int)EnhancementOutcome.None].Should().Be(0);
        }
    }

    [Fact]
    public void Resolve_WithProtection_WardStopsDowngradesAndBlessedStopsBoth()
    {
        uint ward = s_rules.ItemCatalog.ResolveId("forge_ward_scroll");
        uint blessed = s_rules.ItemCatalog.ResolveId("blessed_forge_scroll");
        Table.TryGetProtection(ward, out EnhancementProtection wardScroll).Should().BeTrue();
        Table.TryGetProtection(blessed, out EnhancementProtection blessedScroll).Should().BeTrue();
        Table.TryGetProtection(s_rules.ItemCatalog.ResolveId("iron_ore"), out _).Should().BeFalse();

        EnhancementStep top = Table.Steps[^1];
        int downgradeRoll = top.SuccessPermille + top.NoChangePermille;
        int destroyRoll = EnhancementTable.WeightTotal - 1;
        EnhancementTable.Resolve(top, downgradeRoll).Should().Be(EnhancementOutcome.Downgrade);
        EnhancementTable.Resolve(top, destroyRoll).Should().Be(EnhancementOutcome.Destroyed);

        EnhancementTable.Resolve(top, downgradeRoll, wardScroll).Should().Be(EnhancementOutcome.NoChange);
        EnhancementTable.Resolve(top, destroyRoll, wardScroll).Should().Be(EnhancementOutcome.Destroyed, "a ward does not stop destruction");
        EnhancementTable.Resolve(top, downgradeRoll, blessedScroll).Should().Be(EnhancementOutcome.NoChange);
        EnhancementTable.Resolve(top, destroyRoll, blessedScroll).Should().Be(EnhancementOutcome.NoChange);
        EnhancementTable.Resolve(top, 0, blessedScroll).Should().Be(EnhancementOutcome.Success, "protection never blocks a success");

        wardScroll.AppliesTo(Table.Steps[Table.DowngradeMinLevel]).Should().BeTrue();
        wardScroll.AppliesTo(Table.Steps[Table.DowngradeMinLevel - 1]).Should().BeFalse("scrolls are refused where nothing can be lost");
    }

    [Theory]
    [InlineData("\"success\": 1000,", "\"success\": 999,", "weights must sum to 1000")]
    [InlineData("\"item\": \"stone_chunk\"", "\"item\": \"no_such_item\"", "materials must exist in items.json")]
    [InlineData("\"maxLevel\": 10", "\"maxLevel\": 11", "the level cap is 10")]
    [InlineData("\"destroyMinLevel\": 8", "\"destroyMinLevel\": 9", "+8 rows must not destroy below the threshold")]
    [InlineData("\"item\": \"blessed_forge_scroll\"", "\"item\": \"forge_ward_scroll\"", "a scroll is listed once")]
    public void LoadFromJson_InvalidDocument_IsRejected(string find, string replace, string because)
    {
        ArgumentNullException.ThrowIfNull(find);
        s_shippedJson.Should().Contain(find);
        int at = s_shippedJson.IndexOf(find, StringComparison.Ordinal);
        string broken = string.Concat(s_shippedJson.AsSpan(0, at), replace, s_shippedJson.AsSpan(at + find.Length));

        Action load = () => EnhancementTable.LoadFromJson(broken, s_rules.ItemCatalog);

        load.Should().Throw<InvalidDataException>(because);
    }

    [Fact]
    public void Compile_SafeLevelRowThatDowngrades_IsRejected()
    {
        EnhancementDocument document = JsonSerializer.Deserialize(s_shippedJson, ItemsJsonContext.Default.EnhancementDocument)!;
        EnhancementLevelDefinition[] levels = [.. document.Levels];
        levels[4] = levels[4] with { NoChange = levels[4].NoChange - 50, Downgrade = 50 };

        Action compile = () => EnhancementTable.Compile(document with { Levels = levels }, s_rules.ItemCatalog);

        compile.Should().Throw<InvalidDataException>().WithMessage("*below downgradeMinLevel*");
    }

    [Fact]
    public void TryGetStep_NonGearOrMaxLevel_HasNoStep()
    {
        uint ore = s_rules.ItemCatalog.ResolveId("iron_ore");
        ItemEnhancementPolicy.TryGetStep(s_rules, new Containers.ItemSlot(ore, 5), out _).Should().BeFalse("only gear with durability can be enhanced");
        ItemEnhancementPolicy.TryGetStep(s_rules, new Containers.ItemSlot(StarterGameCatalog.IronSword, 1, UpgradeLevel: 10), out _).Should().BeFalse();
        ItemEnhancementPolicy.TryGetStep(s_rules, new Containers.ItemSlot(StarterGameCatalog.IronSword, 1, UpgradeLevel: 9), out EnhancementStep? last).Should().BeTrue();
        last!.FromLevel.Should().Be(9);
    }
}
