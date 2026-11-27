// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;

namespace Carbon.Desktop.Rendering.Atmospherics;

/// <summary>
/// One shared, deterministic wind for everything that sways (docs/art/graphics-quality.md, foliage): a slow overall
/// strength that rain whips up, gust fronts that roll across the world from west to east (so a forest ripples rather
/// than swaying in unison), and a fast per-plant flutter. A pure function of time, position and rain: cosmetic only,
/// allocation-free, and identical on every client.
/// </summary>
internal static class WindField
{
    /// <summary>Gets the overall wind strength (about 0.3 calm to 1 breezy dry, up to about 2 in a downpour).</summary>
    /// <param name="time">Animation time in seconds.</param>
    /// <param name="rain">Rain intensity, 0..1.</param>
    /// <returns>The strength multiplier.</returns>
    public static float Strength(float time, float rain)
    {
        float breeze = 0.62f + (0.24f * MathF.Sin(time * 0.11f)) + (0.14f * MathF.Sin((time * 0.047f) + 1.7f));
        return breeze * (1f + (1.1f * Math.Clamp(rain, 0f, 1f)));
    }

    /// <summary>Gets the gust front at a position, 0 (lull) to 1 (gust), travelling east at about 200 px/s.</summary>
    /// <param name="time">Animation time in seconds.</param>
    /// <param name="position">World position.</param>
    /// <returns>The gust factor.</returns>
    public static float Gust(float time, Vector2 position)
    {
        float wave = 0.5f + (0.5f * MathF.Sin((time * 0.9f) - (position.X * 0.0045f) - (position.Y * 0.0015f)));
        return wave * wave;
    }

    /// <summary>
    /// Gets how far a canopy leans downwind (east, +x) in world pixels for a plant of the given size. Always leaning a
    /// little with the wind, swinging further in a gust, with a small flutter of its own.
    /// </summary>
    /// <param name="time">Animation time in seconds.</param>
    /// <param name="position">The plant's world position.</param>
    /// <param name="rain">Rain intensity, 0..1.</param>
    /// <param name="scale">The plant's drawn scale (young trees sway less).</param>
    /// <returns>The horizontal lean in world pixels.</returns>
    public static float Sway(float time, Vector2 position, float rain, float scale)
    {
        float flutter = MathF.Sin((time * 2.3f) + (position.X * 0.031f) + (position.Y * 0.017f));
        float swing = MathF.Sin((time * 1.25f) + (position.X * 0.013f) + (position.Y * 0.007f));
        float strength = Strength(time, rain);
        float gust = Gust(time, position);
        return strength * scale * (0.6f + (1.9f * gust) + (0.9f * swing * (0.4f + gust)) + (0.3f * flutter));
    }
}
