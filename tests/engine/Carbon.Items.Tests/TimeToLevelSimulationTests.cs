// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Xunit;

namespace Carbon.Items.Tests;

/// <summary>
/// Verifies the experience curve against the time-to-level targets in docs/progression.md §5, using the deterministic
/// <see cref="TimeToLevelSimulation"/> over the shipped configs.
/// </summary>
public sealed class TimeToLevelSimulationTests
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
}
