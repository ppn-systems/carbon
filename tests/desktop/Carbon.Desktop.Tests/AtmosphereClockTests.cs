// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Numerics;
using Carbon.Desktop.Rendering.Atmospherics;
using Carbon.Desktop.Rendering.World;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>
/// Online, the server owns the hour. The atmosphere used to be stepped with zero time then, which froze rain,
/// splashes, cloud drift and fireflies for every online player. Its animation now runs on frame time regardless.
/// </summary>
public sealed class AtmosphereClockTests
{
    [Fact]
    public void AdvanceAtmosphere_ServerOwnsTheHour_StillAnimates()
    {
        Atmosphere atmosphere = new(23f, 1440f, forcedRain: null);

        for (int frame = 0; frame < 60; frame++)
        {
            WorldSceneRenderer.AdvanceAtmosphere(atmosphere, authoritativeHour: 23f, frameDt: 1f / 60f, timeScale: 1f);
        }

        atmosphere.AnimationTime.Should().BeApproximately(1f, 1e-3f, "a second of frames moves rain, clouds and fireflies by a second");
        atmosphere.Hour.Should().Be(23f, "the hour stays the server's");
    }

    [Fact]
    public void AdvanceAtmosphere_ServerOwnsTheHour_FirefliesMove()
    {
        Atmosphere atmosphere = new(23f, 1440f, forcedRain: false);
        WorldSceneRenderer.AdvanceAtmosphere(atmosphere, 23f, 1f / 60f, 1f);
        List<PointLight> before = new();
        atmosphere.CollectFireflyLights(before, Vector2.Zero);

        for (int frame = 0; frame < 30; frame++)
        {
            WorldSceneRenderer.AdvanceAtmosphere(atmosphere, 23f, 1f / 60f, 1f);
        }

        List<PointLight> after = new();
        atmosphere.CollectFireflyLights(after, Vector2.Zero);
        before.Should().NotBeEmpty("fireflies come out at 23:00 in clear weather");
        after.Should().HaveSameCount(before);
        after[0].Position.Should().NotBe(before[0].Position, "fireflies drift between frames online");
    }

    [Fact]
    public void AdvanceAtmosphere_ServerOwnsTheHour_KeepsTheWeatherWhereItWas()
    {
        Atmosphere atmosphere = new(12f, 1440f, forcedRain: null);
        WorldSceneRenderer.AdvanceAtmosphere(atmosphere, 12f, 1f / 60f, 1f);
        float rain = atmosphere.Rain;

        for (int frame = 0; frame < 6000; frame++)
        {
            WorldSceneRenderer.AdvanceAtmosphere(atmosphere, 12f, 1f / 60f, 1f);
        }

        atmosphere.Rain.Should().Be(rain, "online weather is unchanged by this fix (each client would otherwise roll its own rain)");
    }

    [Fact]
    public void AdvanceAtmosphere_Offline_RunsTheDayCycleAtTheTimeScale()
    {
        Atmosphere atmosphere = new(6f, 1440f, forcedRain: false);

        WorldSceneRenderer.AdvanceAtmosphere(atmosphere, authoritativeHour: null, frameDt: 1f, timeScale: 60f);

        atmosphere.Hour.Should().BeApproximately(7f, 1e-3f, "60 s of a 1440 s day is one hour");
        atmosphere.AnimationTime.Should().BeApproximately(60f, 1e-3f);
    }
}
