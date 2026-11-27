// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using FluentAssertions;
using Xunit;

namespace Carbon.Items.Tests;

/// <summary>ADR-035: the one wear rule. Iron sword: 200 max (300 at +5), so 10 % is 20 (30 enhanced).</summary>
public sealed class DurabilityWearTests
{
    private static readonly ItemRules s_rules = ItemRules.LoadShippedDefaults();

    [Fact]
    public void Apply_StepAboveZero_LowersDurabilityOnly()
    {
        DurabilityWearResult wear = DurabilityWear.Apply(s_rules, Sword(100f), 1.5f);

        wear.Should().Be(new DurabilityWearResult(Sword(98.5f), Changed: true, Broke: false, ReachedCritical: false));
    }

    [Theory]
    [InlineData(1f, 1.5f)]
    [InlineData(1.5f, 1.5f)]
    public void Apply_StepReachingZero_DestroysTheItem(float durability, float loss)
    {
        DurabilityWearResult wear = DurabilityWear.Apply(s_rules, Sword(durability), loss);

        wear.Broke.Should().BeTrue("a step that takes durability to 0 or below breaks the item");
        wear.Slot.Should().Be(ItemSlot.Empty, "a broken item no longer exists");
        wear.ReachedCritical.Should().BeFalse("a break is reported as a break, not as a warning");
    }

    [Fact]
    public void Apply_EnhancedItem_BreaksAndWarnsAgainstItsEnhancedMaximum()
    {
        DurabilityWear.Apply(s_rules, Sword(31f, level: 5), 1.5f).ReachedCritical.Should().BeTrue("31 → 29.5 crosses 10 % of 300");
        DurabilityWear.Apply(s_rules, Sword(21f, level: 5), 1.5f).ReachedCritical.Should().BeFalse("21 is already below the enhanced 10 %");
        DurabilityWear.Apply(s_rules, Sword(1f, level: 10), 1.5f).Broke.Should().BeTrue("enhancement does not protect from breaking");
    }

    [Theory]
    [InlineData(21f, true)]
    [InlineData(20.5f, true)]
    [InlineData(20f, false)]
    [InlineData(19f, false)]
    public void Apply_CriticalShare_IsReportedOnlyOnTheStepThatCrossesIt(float durability, bool expected) =>
        DurabilityWear.Apply(s_rules, Sword(durability), 1f).ReachedCritical.Should().Be(expected);

    [Fact]
    public void Apply_ItemWithoutDurabilityOrNoLoss_ChangesNothing()
    {
        ItemSlot logs = new(StarterGameCatalog.WoodLog, 5);

        DurabilityWear.Apply(s_rules, logs, 3f).Should().Be(new DurabilityWearResult(logs, false, false, false));
        DurabilityWear.Apply(s_rules, Sword(50f), 0f).Changed.Should().BeFalse();
        DurabilityWear.Apply(s_rules, ItemSlot.Empty, 3f).Changed.Should().BeFalse();
    }

    private static ItemSlot Sword(float durability, byte level = 0) =>
        new(StarterGameCatalog.IronSword, 1, InstanceId: 3, Durability: durability, UpgradeLevel: level);
}
