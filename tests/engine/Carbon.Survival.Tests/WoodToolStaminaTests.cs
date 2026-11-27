// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Items.Catalog;
using Carbon.Items.Definitions;
using Carbon.Survival.Stamina;
using FluentAssertions;
using Xunit;

namespace Carbon.Survival.Tests;

/// <summary>The light wood tools cost less stamina per swing than their heavier counterparts (shipped stamina.json).</summary>
public sealed class WoodToolStaminaTests
{
    private static readonly ItemRules s_rules = ItemRules.LoadShippedDefaults();
    private static readonly StaminaConfig s_stamina = StaminaConfig.LoadShippedDefaults();

    [Theory]
    [InlineData("wood_axe", "stone_axe")]
    [InlineData("wood_pickaxe", "stone_pickaxe")]
    [InlineData("wood_sword", "copper_sword")]
    public void GetSwingCost_WoodTool_CostsLessThanTheNextTier(string woodCode, string heavierCode)
    {
        float wood = s_stamina.GetSwingCost(Archetype(woodCode));
        float heavier = s_stamina.GetSwingCost(Archetype(heavierCode));

        wood.Should().BePositive($"swinging {woodCode} still costs stamina");
        wood.Should().BeLessThan(heavier, $"{woodCode} is lighter than {heavierCode}");
    }

    private static ItemArchetype Archetype(string code) => s_rules.Items.GetRequired(s_rules.ItemCatalog.ResolveId(code));
}
