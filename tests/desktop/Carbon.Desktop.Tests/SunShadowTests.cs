// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Desktop.Rendering.Atmospherics;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>
/// Sun shadows follow the clock (docs/art/graphics-quality.md): long to the west in the morning, short at noon, long to
/// the east toward sunset, faint under the moon, softened by rain.
/// </summary>
public sealed class SunShadowTests
{
    [Fact]
    public void At_Morning_FallsWestAndLong()
    {
        SunShadow.State morning = SunShadow.At(7.5f, 0f);
        SunShadow.State noon = SunShadow.At(12f, 0f);

        morning.Direction.X.Should().BeLessThan(-0.5f, "the sun rises in the east, so shadows point west");
        morning.Length.Should().BeGreaterThan(noon.Length * 2f, "a low sun casts long shadows");
    }

    [Fact]
    public void At_Evening_FallsEast()
    {
        SunShadow.State evening = SunShadow.At(17f, 0f);

        evening.Direction.X.Should().BeGreaterThan(0.5f, "toward sunset shadows point east");
    }

    [Fact]
    public void At_Noon_IsShortAndFull()
    {
        SunShadow.State noon = SunShadow.At(12f, 0f);

        MathF.Abs(noon.Direction.X).Should().BeLessThan(0.05f);
        noon.Length.Should().BeApproximately(0.3f, 0.01f);
        noon.Opacity.Should().Be(1f);
    }

    [Fact]
    public void At_Night_IsAFaintMoonShadow()
    {
        SunShadow.State night = SunShadow.At(0f, 0f);

        night.Opacity.Should().BeGreaterThan(0f).And.BeLessThan(0.3f, "moonlight casts only a faint shadow");
    }

    [Fact]
    public void At_Rain_SoftensTheShadow() =>
        SunShadow.At(12f, 1f).Opacity.Should().BeLessThan(SunShadow.At(12f, 0f).Opacity * 0.5f, "overcast skies wash shadows out");

    [Fact]
    public void At_AcrossTheDay_ChangesSmoothly()
    {
        SunShadow.State previous = SunShadow.At(0f, 0f);
        for (float hour = 0.05f; hour < 24f; hour += 0.05f)
        {
            SunShadow.State current = SunShadow.At(hour, 0f);
            MathF.Abs(current.Opacity - previous.Opacity).Should().BeLessThan(0.06f, $"no shadow pops in or out (hour {hour:F2})");
            current.Direction.Length().Should().BeApproximately(1f, 1e-4f);
            previous = current;
        }
    }

    [Fact]
    public void At_EveryFrame_AllocatesNothing()
    {
        _ = SunShadow.At(9f, 0f);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; i++)
        {
            _ = SunShadow.At(i * 0.012f, 0.2f);
        }

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().Be(0);
    }
}
