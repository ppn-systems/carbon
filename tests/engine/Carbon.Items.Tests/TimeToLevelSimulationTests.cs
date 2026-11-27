// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Globalization;
using System.Text;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Carbon.Items.Tests;

/// <summary>
/// Verifies the experience curve against the time-to-level targets in docs/progression.md §5, using the deterministic
/// <see cref="TimeToLevelSimulation"/> over the shipped configs.
/// </summary>
public sealed class TimeToLevelSimulationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1, 3f)]
    [InlineData(5, 6f)]
    [InlineData(9, 9.5f)]
    [InlineData(19, 17f)]
    [InlineData(29, 27f)]
    [InlineData(39, 38f)]
    [InlineData(49, 49f)]
    [InlineData(64, 72f)]
    [InlineData(79, 93f)]
    [InlineData(98, 147f)]
    public void Simulate_BalancedBuild_LevelTakesTargetMinutes(int level, float targetMinutes)
    {
        SimulatedLevel result = TimeToLevelSimulation.Simulate(level, TimeToLevelSimulation.Balanced);
        result.MinutesToNext.Should().BeInRange(
            targetMinutes * 0.7f,
            targetMinutes * 1.3f,
            $"level {level}->{level + 1} targets ~{targetMinutes} min of play (±30%)");
    }

    [Theory]
    [InlineData(1, 10, 60f)]
    [InlineData(10, 20, 135f)]
    [InlineData(20, 30, 225f)]
    [InlineData(30, 50, 780f)]
    [InlineData(50, 80, 2_200f)]
    [InlineData(80, 99, 2_300f)]
    public void MinutesBetween_BalancedBuild_BracketTakesTargetTime(int from, int to, float targetMinutes)
    {
        float minutes = TimeToLevelSimulation.MinutesBetween(from, to, TimeToLevelSimulation.Balanced);
        minutes.Should().BeInRange(targetMinutes * 0.8f, targetMinutes * 1.2f, $"levels {from}->{to} target ~{targetMinutes} min (±20%)");
    }

    [Fact]
    public void MinutesBetween_WholeCurve_TakesAboutOneHundredHours()
    {
        float hours = TimeToLevelSimulation.MinutesBetween(1, TimeToLevelSimulation.Config.MaxLevel, TimeToLevelSimulation.Balanced) / 60f;
        hours.Should().BeInRange(80f, 120f, "level 1->99 targets ~100 hours of play");
    }

    [Fact]
    public void Simulate_EveryLevel_KillRateMatchesEconomyModelAndFightsStaySurvivable()
    {
        for (int lv = 1; lv < TimeToLevelSimulation.Config.MaxLevel; lv++)
        {
            SimulatedLevel r = TimeToLevelSimulation.Simulate(lv, TimeToLevelSimulation.Balanced);
            r.KillsPerHour.Should().BeInRange(60f, 240f, $"economy.json assumes ~120 kills/hour (level {lv})");
            r.DayTimeToKillSeconds.Should().BeInRange(1f, 8f, $"a level-appropriate monster should die in seconds (level {lv})");
            r.HealthLostPerKill.Should().BeLessThan(0.5f, $"one on-level fight must not cost half the health bar (level {lv})");
        }
    }

    [Fact]
    public void Simulate_AlternativeBuilds_StayWithinSeventyPercentOfBalanced()
    {
        SimulatedBuild[] builds = [TimeToLevelSimulation.Tank, TimeToLevelSimulation.GlassCannon, TimeToLevelSimulation.Skirmisher];
        foreach (int lv in new[] { 5, 15, 30, 50, 70, 90 })
        {
            float balanced = TimeToLevelSimulation.Simulate(lv, TimeToLevelSimulation.Balanced).MinutesToNext;
            foreach (SimulatedBuild build in builds)
            {
                float minutes = TimeToLevelSimulation.Simulate(lv, build).MinutesToNext;
                (minutes / balanced).Should().BeInRange(0.6f, 1.7f, $"the {build.Name} build at level {lv} must stay viable without dominating");
            }
        }
    }

    [Fact]
    public void Simulate_Report_PrintsTable()
    {
        StringBuilder sb = new();
        sb.AppendLine("lvl | kills/h | xp/min | min->next | TTK day s | hp lost/kill | tank | glass | skirm");
        foreach (int lv in new[] { 1, 2, 5, 9, 10, 15, 19, 20, 25, 29, 30, 39, 40, 49, 50, 60, 64, 70, 79, 80, 90, 98 })
        {
            SimulatedLevel r = TimeToLevelSimulation.Simulate(lv, TimeToLevelSimulation.Balanced);
            sb.AppendLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{lv,3} | {r.KillsPerHour,7:F0} | {r.XpPerMinute,6:F0} | {r.MinutesToNext,9:F1} | {r.DayTimeToKillSeconds,9:F1} | {r.HealthLostPerKill * 100f,11:F0}% | {TimeToLevelSimulation.Simulate(lv, TimeToLevelSimulation.Tank).MinutesToNext,4:F0} | {TimeToLevelSimulation.Simulate(lv, TimeToLevelSimulation.GlassCannon).MinutesToNext,5:F0} | {TimeToLevelSimulation.Simulate(lv, TimeToLevelSimulation.Skirmisher).MinutesToNext,5:F0}"));
        }

        foreach ((int a, int b) in new[] { (1, 10), (10, 20), (20, 30), (30, 50), (50, 80), (80, 99), (1, 99) })
        {
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{a}->{b}: {TimeToLevelSimulation.MinutesBetween(a, b, TimeToLevelSimulation.Balanced) / 60f:F1} h"));
        }

        output.WriteLine(sb.ToString());
        sb.Length.Should().BePositive();
    }
}
