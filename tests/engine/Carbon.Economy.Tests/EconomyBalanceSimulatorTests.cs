// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using Carbon.Economy.Config;
using Carbon.Economy.Simulation;
using Carbon.Items.Definitions;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Carbon.Economy.Tests;

/// <summary>
/// Player-hour balance gate: plays the <c>economy.json → playerModel</c> against the shipped loot, vendor, repair and
/// station-fee tables and fails when net gold/hour or the sink share leaves its designed band.
/// </summary>
public sealed class EconomyBalanceSimulatorTests
{
    private const int Hours = 20;
    private static readonly int[] s_levels = [1, 5, 9, 10, 15, 20, 24, 25, 30, 35, 40, 45, 49, 50, 55, 60, 65, 70, 74, 75, 80, 85, 90, 95, 100];
    private readonly ITestOutputHelper _output;

    public EconomyBalanceSimulatorTests(ITestOutputHelper output) => _output = output;

    private static GameEconomy Economy => GameEconomy.Load(ShippedConfig.Read("items.json"), ShippedConfig.Read("recipes.json"), ShippedConfig.Read("economy.json"), ShippedConfig.Read("loot.json"));

    [Fact]
    public void Simulate_PlayerHoursAtEveryStage_NetGoldAndSinkShareStayInsideDesignBands()
    {
        EconomySimulator simulator = new(Economy);
        List<EconomyHourReport> reports = [.. s_levels.Select(level => simulator.Simulate(level, Hours, TestGameplayKeys.Key(2026)))];
        _output.WriteLine(EconomySimulator.FormatTable(reports));
        DesignTargets targets = ShippedConfig.Economy.DesignTargets;

        foreach (EconomyHourReport r in reports)
        {
            LevelBand net = targets.NetGoldPerHour.Single(b => b.MinLevel <= r.Level && r.Level <= b.MaxLevel);
            LevelBand share = targets.SinkShare.Single(b => b.MinLevel <= r.Level && r.Level <= b.MaxLevel);
            r.Net.Should().BeInRange(net.Min, net.Max, "net gold/hour at level {0}", r.Level);
            r.SinkShare.Should().BeInRange(share.Min, share.Max, "sink share at level {0}", r.Level);
            r.Repairs.Should().BePositive("every stage's weapon must wear out and be repaired at level {0}", r.Level);
            r.VendorPurchases.Should().BePositive("consumables are bought every hour at level {0}", r.Level);
        }

        EconomySimulator.InflationSlope(reports).Should().BeInRange(
            targets.InflationSlope.Min,
            targets.InflationSlope.Max,
            "net gold/hour may grow with level, but only inside designTargets.inflationSlope");
    }

    [Fact]
    public void Simulate_SameSeed_IsDeterministic()
    {
        EconomySimulator simulator = new(Economy);

        simulator.Simulate(30, 3, TestGameplayKeys.Key(7)).Should().Be(simulator.Simulate(30, 3, TestGameplayKeys.Key(7)));
    }

    [Fact]
    public void PlayerModel_Stages_CoverEveryLevelWithoutGaps()
    {
        PlayerModel model = ShippedConfig.Economy.PlayerModel;

        for (int level = 1; level <= ShippedConfig.Economy.MaxLevel; level++)
        {
            model.Stages.Count(s => s.MinLevel <= level && level <= s.MaxLevel).Should().Be(1, "level {0}", level);
        }

        foreach (PlayerStage stage in model.Stages)
        {
            ShippedConfig.Items.TryGetByCode(stage.Weapon, out ItemCatalogEntry? weapon).Should().BeTrue("weapon '{0}' must exist in items.json", stage.Weapon);
            weapon!.Category.Should().Be(ItemCategory.Weapon, "'{0}' is the stage's weapon", stage.Weapon);
            stage.Consumables.Where(use => !ShippedConfig.Items.TryGetByCode(use.Item, out ItemCatalogEntry? item) || !item.VendorBuyable)
                .Should().BeEmpty("consumables must be vendor-buyable items in items.json");
            stage.Recipes.Where(code => !ShippedConfig.Recipes.TryGetByCode(code, out _)).Should().BeEmpty("recipes must exist in recipes.json");
        }
    }
}
