// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Desktop.Rendering.PostProcessing;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>
/// The time-of-day colour grading curves (ADR-041, docs/art/graphics-quality.md): vivid noon, warm golden hour, cool and
/// desaturated night, greyer rain, and no visible jumps as the clock runs.
/// </summary>
public sealed class TimeOfDayGradingTests
{
    [Fact]
    public void Sample_Noon_IsMoreSaturatedThanNight()
    {
        ColorGrade noon = TimeOfDayGrading.Sample(12f, 0f);
        ColorGrade night = TimeOfDayGrading.Sample(23f, 0f);

        noon.Saturation.Should().BeGreaterThan(1f, "midday art reads crisp and slightly vivid");
        night.Saturation.Should().BeLessThan(0.8f, "the eye loses colour first in the dark");
        night.Vignette.Should().BeGreaterThan(noon.Vignette, "night closes in from the edges");
    }

    [Fact]
    public void Sample_Night_LiftsShadowsTowardBlue()
    {
        ColorGrade night = TimeOfDayGrading.Sample(2f, 0f);

        night.Lift.Z.Should().BeGreaterThan(night.Lift.X, "night shadows are moonlit blue, not black");
        night.Apply(Vector3.Zero).Z.Should().BeGreaterThan(night.Apply(Vector3.Zero).X);
    }

    [Fact]
    public void Sample_GoldenHour_WarmsHighlights()
    {
        ColorGrade golden = TimeOfDayGrading.Sample(18f, 0f);
        Vector3 white = golden.Apply(new Vector3(0.8f));

        golden.Gain.X.Should().BeGreaterThan(golden.Gain.Z);
        white.X.Should().BeGreaterThan(white.Z, "late sun turns a grey wall amber");
    }

    [Fact]
    public void Sample_AcrossTheWholeDay_HasNoVisibleJumps()
    {
        ColorGrade previous = TimeOfDayGrading.Sample(0f, 0f);
        for (float hour = 0.05f; hour <= 24f; hour += 0.05f)
        {
            ColorGrade current = TimeOfDayGrading.Sample(hour, 0f);
            float jump = (current.Lift - previous.Lift).Length() + (current.Gain - previous.Gain).Length()
                + MathF.Abs(current.Saturation - previous.Saturation) + MathF.Abs(current.Contrast - previous.Contrast);
            jump.Should().BeLessThan(0.03f, $"the grade must ease between keyframes (hour {hour:F2})");
            previous = current;
        }
    }

    [Theory]
    [InlineData(-12f, 12f)]
    [InlineData(30f, 6f)]
    [InlineData(42.5f, 18.5f)]
    public void Sample_HourOutsideTheDay_Wraps(float hour, float wrapped) =>
        TimeOfDayGrading.Sample(hour, 0f).Should().Be(TimeOfDayGrading.Sample(wrapped, 0f));

    [Fact]
    public void Sample_Rain_GreysAndCoolsTheHour()
    {
        ColorGrade dry = TimeOfDayGrading.Sample(12f, 0f);
        ColorGrade wet = TimeOfDayGrading.Sample(12f, 1f);

        wet.Saturation.Should().BeLessThan(dry.Saturation, "rain greys the world");
        wet.Contrast.Should().BeLessThan(dry.Contrast, "rain flattens contrast");
        (wet.Gain.Z / wet.Gain.X).Should().BeGreaterThan(dry.Gain.Z / dry.Gain.X, "rain cools the light");
    }

    [Fact]
    public void Sample_RainAtNight_StaysNight()
    {
        ColorGrade night = TimeOfDayGrading.Sample(23f, 0f);
        ColorGrade wetNight = TimeOfDayGrading.Sample(23f, 1f);

        wetNight.Saturation.Should().BeLessThan(night.Saturation);
        wetNight.Vignette.Should().BeGreaterThanOrEqualTo(night.Vignette, "rain never lightens the night vignette");
    }

    [Fact]
    public void Identity_Apply_LeavesColoursUnchanged()
    {
        Vector3 colour = new(0.2f, 0.55f, 0.9f);

        Vector3 graded = ColorGrade.Identity.Apply(colour);

        (graded - colour).Length().Should().BeLessThan(1e-5f);
    }

    [Fact]
    public void Sample_EveryFrame_AllocatesNothing()
    {
        _ = TimeOfDayGrading.Sample(6.2f, 0.3f);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; i++)
        {
            _ = TimeOfDayGrading.Sample(i * 0.013f, i % 10 * 0.1f);
        }

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().Be(0, "the grade is sampled every frame");
    }
}
