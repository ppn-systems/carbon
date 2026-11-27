// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Maps.Biomes;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

public sealed class DesktopOptionsTests
{
    [Fact]
    public void FromEnvironment_FocusBiomeAndAutoWalk_AreParsed()
    {
        Dictionary<string, string> env = new()
        {
            ["CARBON_FOCUS_BIOME"] = "redmesa",
            ["CARBON_AUTO_WALK"] = "1,0.5",
        };

        DesktopOptions options = DesktopOptions.FromEnvironment(k => env.GetValueOrDefault(k));

        options.FocusBiome.Should().Be(BiomeType.RedMesa);
        options.AutoWalk.Should().Be((1f, 0.5f));
    }

    [Fact]
    public void FromEnvironment_UnknownFocusBiome_IsIgnored()
    {
        DesktopOptions options = DesktopOptions.FromEnvironment(k => k == "CARBON_FOCUS_BIOME" ? "Moon" : null);

        options.FocusBiome.Should().BeNull();
        options.AutoWalk.Should().BeNull();
    }

    [Theory]
    [InlineData("0.55", 0.55f)]
    [InlineData("500", 60f)]
    [InlineData("-1", 0f)]
    [InlineData("fast", 0f)]
    [InlineData(null, 0f)]
    public void FromEnvironment_AutoAttack_ParsesClampedSeconds(string? value, float expected)
    {
        DesktopOptions options = DesktopOptions.FromEnvironment(k => k == "CARBON_AUTO_ATTACK" ? value : null);

        options.AutoAttackSeconds.Should().BeApproximately(expected, 1e-4f, "CARBON_AUTO_ATTACK is seconds between swings, 0 = off");
    }
}
