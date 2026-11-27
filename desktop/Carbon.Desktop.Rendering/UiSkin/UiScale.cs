// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;

namespace Carbon.Desktop.Rendering.UiSkin;

/// <summary>
/// How many screen pixels one UI layout unit covers. Layouts are designed for a 1280×720 canvas; a larger window draws
/// that canvas larger (fonts re-baked at the scale, vector shapes scaled, nine-slice corners at whole pixels) instead of
/// leaving 14 px text in the middle of a 1080p screen. Auto picks the largest preset whose canvas still fits the window:
/// 720p gives 1, 1080p gives 1.5, 1440p gives 2.
/// </summary>
public static class UiScale
{
    /// <summary>The canvas width layouts are designed for.</summary>
    public const float CanvasWidth = 1280f;

    /// <summary>The canvas height layouts are designed for.</summary>
    public const float CanvasHeight = 720f;

    /// <summary>The setting value meaning "pick from the window size".</summary>
    public const string AutoValue = "auto";

    private static readonly float[] s_presets = [1f, 1.25f, 1.5f, 2f];

    /// <summary>Gets the scales a player can choose (Settings offers Auto plus these).</summary>
    public static ReadOnlySpan<float> Presets => s_presets;

    /// <summary>
    /// Gets the largest preset whose 1280×720 canvas fits a <paramref name="width"/> × <paramref name="height"/> window,
    /// or 1 when even that does not fit.
    /// </summary>
    /// <param name="width">Window width in pixels.</param>
    /// <param name="height">Window height in pixels.</param>
    /// <returns>The scale.</returns>
    public static float Fit(int width, int height)
    {
        float best = 1f;
        foreach (float preset in s_presets)
        {
            if (CanvasWidth * preset <= width && CanvasHeight * preset <= height)
            {
                best = preset;
            }
        }

        return best;
    }

    /// <summary>
    /// Resolves the scale to draw at: the player's <paramref name="choice"/>, or <see cref="Fit"/> for Auto
    /// (<see langword="null"/>). A choice larger than the window fits falls back to the fit, so a menu is never cut off.
    /// </summary>
    /// <param name="choice">The chosen preset, or <see langword="null"/> for Auto.</param>
    /// <param name="width">Window width in pixels.</param>
    /// <param name="height">Window height in pixels.</param>
    /// <returns>The scale.</returns>
    public static float Resolve(float? choice, int width, int height)
    {
        float fit = Fit(width, height);
        return choice is { } chosen ? MathF.Min(chosen, fit) : fit;
    }

    /// <summary>Parses a <c>ui_scale</c> setting: <c>auto</c> or one of <see cref="Presets"/>.</summary>
    /// <param name="value">The setting text.</param>
    /// <param name="choice">The preset, or <see langword="null"/> for Auto.</param>
    /// <returns><see langword="true"/> for <c>auto</c> or a preset; <see langword="false"/> for anything else.</returns>
    public static bool TryParse(string? value, out float? choice)
    {
        choice = null;
        if (string.Equals(value?.Trim(), AutoValue, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
        {
            return false;
        }

        foreach (float preset in s_presets)
        {
            if (preset == parsed)
            {
                choice = preset;
                return true;
            }
        }

        return false;
    }

    /// <summary>Formats <paramref name="choice"/> for <c>settings.ini</c> (<c>auto</c>, <c>1</c>, <c>1.25</c>, ...).</summary>
    /// <param name="choice">The preset, or <see langword="null"/> for Auto.</param>
    /// <returns>The setting text.</returns>
    public static string ToSettingValue(float? choice) =>
        choice is { } chosen ? chosen.ToString(CultureInfo.InvariantCulture) : AutoValue;
}
