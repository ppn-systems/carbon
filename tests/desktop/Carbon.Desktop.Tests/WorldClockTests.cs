// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
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
    [InlineData(0f, true, nameof(DayPhase.Night))]
    [InlineData(5f, false, nameof(DayPhase.Dawn))]
    [InlineData(6.5f, false, nameof(DayPhase.Dawn))]
    [InlineData(7f, false, nameof(DayPhase.Day))]
    [InlineData(12f, false, nameof(DayPhase.Day))]
    [InlineData(16.99f, false, nameof(DayPhase.Day))]
    [InlineData(17f, false, nameof(DayPhase.Dusk))]
    [InlineData(18.99f, false, nameof(DayPhase.Dusk))]
    [InlineData(19f, true, nameof(DayPhase.Night))]
    [InlineData(23.5f, true, nameof(DayPhase.Night))]
    [InlineData(29f, false, nameof(DayPhase.Dawn))]
    [InlineData(18.99f, true, nameof(DayPhase.Night))] // the session's night always wins over the cosmetic dusk band
    public void GetPhase_HourAndSessionNight_ReturnsPhase(float hour, bool isNight, string expected) =>
        WorldClock.GetPhase(hour, isNight).ToString().Should().Be(expected, $"{hour}h");

    [Theory]
    [InlineData(0UL, nameof(DayPhase.Day), "10:00")] // the world starts at 10:00
    [InlineData(1_400UL, nameof(DayPhase.Dusk), "17:00")] // +70 s = +7 h
    [InlineData(1_800UL, nameof(DayPhase.Night), "19:00")] // +90 s = +9 h: monsters empowered
    [InlineData(3_800UL, nameof(DayPhase.Dawn), "05:00")] // +190 s = +19 h, next morning
    [InlineData(4_800UL, nameof(DayPhase.Day), "10:00")] // a full 240 s day later
    public void GetPhase_ServerTick_FollowsTheServerClock(ulong serverTick, string phase, string time)
    {
        // What MultiplayerSession reports: AuthoritativeTimeHour from the server tick, AuthoritativeIsNight from it.
        float hour = WorldProgression.ComputeTimeOfDayHours(serverTick, TickRate);
        bool isNight = WorldProgression.IsNightHour(hour);
        Span<char> buffer = stackalloc char[5];

        WorldClock.GetPhase(hour, isNight).ToString().Should().Be(phase);
        new string(WorldClock.FormatTime(hour, buffer)).Should().Be(time);
    }

    [Fact]
    public void SessionNightRule_MatchesTheShippedServerMonsterEmpowermentWindow()
    {
        // MultiplayerSession.AuthoritativeIsNight uses WorldProgression.IsNightHour; the server empowers monsters by
        // spawning.json "night". The HUD hint is only honest while the two agree.
        SpawningConfig shipped = SpawningConfig.FromJson(File.ReadAllText(Path.Combine(FindRepositoryRoot(), "resources", "config", "spawning.json")));
        for (float hour = 0f; hour < 24f; hour += 0.25f)
        {
            WorldProgression.IsNightHour(hour).Should().Be(shipped.Night.IsNight(hour), $"the night hint must match the server at {hour}h");
        }
    }

    [Theory]
    [InlineData(nameof(DayPhase.Night), "clock.night_hint")]
    [InlineData(nameof(DayPhase.Dusk), "clock.dusk_hint")]
    [InlineData(nameof(DayPhase.Day), null)]
    [InlineData(nameof(DayPhase.Dawn), null)]
    public void HintKey_Phase_WarnsAtDuskAndNightOnly(string phase, string? expected) =>
        DayNightClockWidget.HintKey(Enum.Parse<DayPhase>(phase)).Should().Be(expected);

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

    [Fact]
    public void DialAngle_NoonAndMidnight_SunAtTopAndBottom()
    {
        WorldClock.DialAngle(12f).Should().BeApproximately(0f, 1e-4f, "the sun is at the top of the dial at noon");
        MathF.Abs(WorldClock.DialAngle(0f)).Should().BeApproximately(MathF.PI, 1e-4f, "the sun is at the bottom at midnight");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Carbon.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Carbon.slnx not found above the test output directory.");
    }
}
