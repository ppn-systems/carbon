// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Creatures.Scaling;
using Carbon.Desktop.Rendering.Hud;
using Carbon.Maps.Generators;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>HUD clock: the phase and time shown for a given server world time.</summary>
public sealed class WorldClockTests
{
    private const ushort TickRate = 20;

    [Theory]
    [InlineData(0f, nameof(DayPhase.Night))]
    [InlineData(4.99f, nameof(DayPhase.Night))]
    [InlineData(5f, nameof(DayPhase.Dawn))]
    [InlineData(6.5f, nameof(DayPhase.Dawn))]
    [InlineData(7f, nameof(DayPhase.Day))]
    [InlineData(12f, nameof(DayPhase.Day))]
    [InlineData(16.99f, nameof(DayPhase.Day))]
    [InlineData(17f, nameof(DayPhase.Dusk))]
    [InlineData(18.99f, nameof(DayPhase.Dusk))]
    [InlineData(19f, nameof(DayPhase.Night))]
    [InlineData(23.5f, nameof(DayPhase.Night))]
    [InlineData(-1f, nameof(DayPhase.Night))]
    [InlineData(29f, nameof(DayPhase.Dawn))]
    public void GetPhase_Hour_ReturnsPhase(float hour, string expected) =>
        WorldClock.GetPhase(hour).ToString().Should().Be(expected, $"{hour}h");

    [Theory]
    [InlineData(0UL, nameof(DayPhase.Day), "10:00")] // the world starts at 10:00
    [InlineData(1_400UL, nameof(DayPhase.Dusk), "17:00")] // +70 s = +7 h
    [InlineData(1_800UL, nameof(DayPhase.Night), "19:00")] // +90 s = +9 h: monsters empowered
    [InlineData(3_800UL, nameof(DayPhase.Dawn), "05:00")] // +190 s = +19 h, next morning
    [InlineData(4_800UL, nameof(DayPhase.Day), "10:00")] // a full 240 s day later
    public void GetPhase_ServerTick_FollowsTheServerClock(ulong serverTick, string phase, string time)
    {
        float hour = WorldProgression.ComputeTimeOfDayHours(serverTick, TickRate);
        Span<char> buffer = stackalloc char[5];

        WorldClock.GetPhase(hour).ToString().Should().Be(phase);
        new string(WorldClock.FormatTime(hour, buffer)).Should().Be(time);
    }

    [Fact]
    public void GetPhase_Night_MatchesTheServerMonsterEmpowermentWindow()
    {
        NightConfig serverNight = new();
        for (float hour = 0f; hour < 24f; hour += 0.25f)
        {
            (WorldClock.GetPhase(hour) == DayPhase.Night).Should().Be(serverNight.IsNight(hour), $"the night hint must match the server at {hour}h");
        }
    }

    [Theory]
    [InlineData(6.4f, "06:24")]
    [InlineData(0f, "00:00")]
    [InlineData(23.999f, "23:59")]
    [InlineData(13.5f, "13:30")]
    public void FormatTime_Hour_FormatsTwentyFourHourClock(float hour, string expected)
    {
        Span<char> buffer = stackalloc char[5];
        new string(WorldClock.FormatTime(hour, buffer)).Should().Be(expected);
    }

    [Theory]
    [InlineData(17.5f, 90)]
    [InlineData(6f, 13 * 60)]
    [InlineData(19f, 0)]
    [InlineData(2f, 0)]
    public void MinutesUntilNight_Hour_CountsDownToNineteen(float hour, int expected) =>
        WorldClock.MinutesUntilNight(hour).Should().Be(expected);

    [Fact]
    public void DialAngle_NoonAndMidnight_SunAtTopAndBottom()
    {
        WorldClock.DialAngle(12f).Should().BeApproximately(0f, 1e-4f, "the sun is at the top of the dial at noon");
        MathF.Abs(WorldClock.DialAngle(0f)).Should().BeApproximately(MathF.PI, 1e-4f, "the sun is at the bottom at midnight");
    }
}
