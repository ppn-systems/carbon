// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;

namespace Carbon.Desktop.Rendering.PostProcessing;

/// <summary>
/// Time-of-day colour grading curves (ADR-041, <c>docs/art/graphics-quality.md</c>): a crisp, slightly saturated
/// noon; warm golden hours; a violet dusk; and a cool, desaturated night with blue shadows (the eye loses colour
/// first in the dark), so lit pools of torchlight read as the only warm colour. Rain greys and cools every grade.
/// </summary>
public static class TimeOfDayGrading
{
    private static readonly ColorGrade s_night = new(new Vector3(0.020f, 0.030f, 0.070f), new Vector3(0.92f, 0.98f, 1.10f), 1.06f, 0.72f, 0.42f);
    private static readonly ColorGrade s_dawn = new(new Vector3(0.035f, 0.020f, 0.040f), new Vector3(1.06f, 0.96f, 0.94f), 1.02f, 0.92f, 0.28f);
    private static readonly ColorGrade s_morning = new(new Vector3(0.010f, 0.008f, 0.012f), new Vector3(1.03f, 1.00f, 0.96f), 1.04f, 1.06f, 0.20f);
    private static readonly ColorGrade s_noon = new(new Vector3(0.000f, 0.004f, 0.012f), new Vector3(1.02f, 1.02f, 1.00f), 1.06f, 1.10f, 0.18f);
    private static readonly ColorGrade s_golden = new(new Vector3(0.030f, 0.012f, 0.000f), new Vector3(1.08f, 0.98f, 0.84f), 1.06f, 1.16f, 0.24f);
    private static readonly ColorGrade s_dusk = new(new Vector3(0.035f, 0.015f, 0.055f), new Vector3(1.04f, 0.94f, 1.00f), 1.05f, 1.04f, 0.30f);

    // Keyframes over a 24 h day; Sample blends neighbours with a smoothstep, like the ambient light table.
    private static readonly (float Hour, ColorGrade Grade)[] s_keys =
    [
        (0f, s_night),
        (4.6f, s_night),
        (6.1f, s_dawn),
        (8.0f, s_morning),
        (12.0f, s_noon),
        (16.2f, s_morning),
        (18.0f, s_golden),
        (19.5f, s_dusk),
        (21.0f, s_night),
        (24f, s_night),
    ];

    private static readonly ColorGrade s_rain = new(new Vector3(0.020f, 0.025f, 0.035f), new Vector3(0.94f, 0.97f, 1.02f), 0.96f, 0.80f, 0.30f);

    /// <summary>Gets the grade for an hour of day and a rain intensity.</summary>
    /// <param name="hour">Hour of day, any value (wrapped to 0..24).</param>
    /// <param name="rain">Rain intensity, 0..1.</param>
    /// <returns>The grade.</returns>
    public static ColorGrade Sample(float hour, float rain)
    {
        float h = ((hour % 24f) + 24f) % 24f;
        ColorGrade grade = s_keys[^1].Grade;
        for (int i = 0; i + 1 < s_keys.Length; i++)
        {
            (float h0, ColorGrade g0) = s_keys[i];
            (float h1, ColorGrade g1) = s_keys[i + 1];
            if (h >= h0 && h <= h1)
            {
                float t = (h - h0) / (h1 - h0);
                grade = ColorGrade.Lerp(in g0, in g1, t * t * (3f - (2f * t)));
                break;
            }
        }

        float wet = Math.Clamp(rain, 0f, 1f) * 0.7f;
        if (wet <= 0f)
        {
            return grade;
        }

        // Rain multiplies the gain and saturation down rather than replacing the hour's grade, so night stays night.
        ColorGrade wetGrade = grade with
        {
            Lift = Vector3.Lerp(grade.Lift, s_rain.Lift, wet),
            Gain = grade.Gain * Vector3.Lerp(Vector3.One, s_rain.Gain, wet),
            Contrast = grade.Contrast * (1f + ((s_rain.Contrast - 1f) * wet)),
            Saturation = grade.Saturation * (1f + ((s_rain.Saturation - 1f) * wet)),
            Vignette = MathF.Max(grade.Vignette, s_rain.Vignette * wet),
        };
        return wetGrade;
    }
}
