// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Linq;
using Carbon.Desktop.Rendering.Hud;
using Carbon.Items.Definitions;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>Owner request: enhanced items get a tiered slot treatment that is distinct from the rarity border.</summary>
public sealed class EnhancementTierStylesTests
{
    [Theory]
    [InlineData(0, 0, "")]
    [InlineData(1, 1, "enhancement.tier.honed")]
    [InlineData(3, 1, "enhancement.tier.honed")]
    [InlineData(4, 2, "enhancement.tier.tempered")]
    [InlineData(6, 2, "enhancement.tier.tempered")]
    [InlineData(7, 3, "enhancement.tier.runed")]
    [InlineData(8, 3, "enhancement.tier.runed")]
    [InlineData(9, 4, "enhancement.tier.exalted")]
    [InlineData(10, 5, "enhancement.tier.mythic")]
    [InlineData(15, 5, "enhancement.tier.mythic")]
    public void Get_EachLevel_MapsToItsTier(byte level, byte tier, string nameKey)
    {
        EnhancementTierStyle style = EnhancementTierStyles.Get(level);

        style.Tier.Should().Be(tier);
        style.NameKey.Should().Be(nameKey);
    }

    [Fact]
    public void Tiers_CoverOneToTenContiguouslyWithGlowFromPlusSevenAndShimmerOnlyAtPlusTen()
    {
        EnhancementTierStyle[] tiers = EnhancementTierStyles.All.ToArray();

        tiers[0].MinLevel.Should().Be(1);
        tiers[^1].MaxLevel.Should().Be(EnhancementTierStyles.MaxLevel);
        for (int i = 1; i < tiers.Length; i++)
        {
            tiers[i].MinLevel.Should().Be((byte)(tiers[i - 1].MaxLevel + 1), "tiers must not overlap or leave gaps");
        }

        tiers.Where(t => t.HasGlow).Select(t => (int)t.MinLevel).Should().Equal(7, 9, 10);
        tiers.Where(t => t.Shimmer).Select(t => (int)t.MinLevel).Should().Equal(10);
    }

    [Fact]
    public void Tiers_FillsAndAccentsAreDistinctFromEachOtherAndFromRarityBorders()
    {
        EnhancementTierStyle[] tiers = EnhancementTierStyles.All.ToArray();

        tiers.Select(t => t.Fill).Distinct().Should().HaveCount(tiers.Length);
        tiers.Select(t => t.Accent).Distinct().Should().HaveCount(tiers.Length);
        foreach (ItemRarity rarity in System.Enum.GetValues<ItemRarity>())
        {
            tiers.Select(t => t.Fill).Should().NotContain(ItemRarityColors.GetBorder(rarity), "enhancement is the fill, rarity is the border");
        }
    }

    [Fact]
    public void Badge_IsCachedSoDrawingDoesNotAllocate()
    {
        EnhancementTierStyles.Badge(7).Should().Be("+7");
        EnhancementTierStyles.Badge(10).Should().Be("+10");
        EnhancementTierStyles.Badge(0).Should().BeEmpty();
        ReferenceEquals(EnhancementTierStyles.Badge(7), EnhancementTierStyles.Badge(7)).Should().BeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(10)]
    public void ItemTooltip_EveryTier_NamesTheTierInItsAccentColour(byte level)
    {
        System.Collections.Generic.List<ItemTooltipLine> lines = [];
        Carbon.Items.Containers.ItemSlot sword = new(Carbon.Items.Catalog.StarterGameCatalog.IronSword, 1, 0, 200f, UpgradeLevel: level);

        ItemTooltipBuilder.Build(new Carbon.Desktop.Abstractions.Configuration.SyncedItemCatalog(), in sword, 15, lines);

        EnhancementTierStyle style = EnhancementTierStyles.Get(level);
        lines.Should().Contain(l => l.Text.Contains(Carbon.Desktop.Abstractions.Localization.GameText.Get(style.NameKey), System.StringComparison.Ordinal) && l.Color == style.Accent);
    }
}
