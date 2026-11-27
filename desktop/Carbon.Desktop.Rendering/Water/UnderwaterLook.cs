// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Desktop.Abstractions.World;
using Carbon.Maps.Hydrology;

namespace Carbon.Desktop.Rendering.Water;

/// <summary>
/// How the world looks from the local player's place in the water (ADR-032 D13): a blue-green tint that darkens and
/// a clear circle of view that narrows as the water deepens, from the surface of Medium water to the abyss floor.
/// A pure function of the water view, so it scales smoothly with depth and is testable without a GPU.
/// </summary>
/// <param name="Strength">0 on land and wading, rising to 1 on the abyss floor.</param>
/// <param name="TintAlpha">Opacity of the full-screen water tint, 0..255.</param>
/// <param name="ClearRadiusFraction">Radius of the clear view as a fraction of the screen's half diagonal.</param>
/// <param name="EdgeAlpha">Opacity of the fog at the screen edge, 0..255.</param>
/// <param name="CausticsAlpha">Opacity of the drifting light caustics, 0..255 (they fade out in the deep).</param>
/// <param name="IsSubmerged">Whether the player is under the surface (diving), which also draws rising bubbles.</param>
internal readonly record struct UnderwaterLook(
    float Strength,
    byte TintAlpha,
    float ClearRadiusFraction,
    byte EdgeAlpha,
    byte CausticsAlpha,
    bool IsSubmerged)
{
    /// <summary>Gets the look of dry land and wading: nothing is drawn.</summary>
    public static UnderwaterLook None { get; } = new(0f, 0, 1f, 0, 0, false);

    /// <summary>Gets whether the filter draws anything.</summary>
    public bool IsVisible => this.Strength > 0f;

    /// <summary>Resolves the look for the local player's water view.</summary>
    /// <param name="water">The water view.</param>
    /// <returns>The look; <see cref="None"/> on land and while wading.</returns>
    public static UnderwaterLook For(in WaterView water)
    {
        if (water.SwimState is not (SwimState.Swimming or SwimState.Diving))
        {
            return None;
        }

        if (water.SwimState == SwimState.Swimming)
        {
            // Head above the surface: a light tint and the caustics on the water, no fog.
            float surface = Math.Clamp((water.Depth - WaterDepth.MediumFrom) / (WaterDepth.DeepFrom - WaterDepth.MediumFrom), 0f, 1f);
            return new UnderwaterLook(0.12f + (0.08f * surface), (byte)(28 + (14 * surface)), 1f, 0, 60, false);
        }

        // Under the surface: from the top of Deep water (0.25) to the abyss floor (1).
        float t = Math.Clamp((water.Depth - WaterDepth.DeepFrom) / (WaterDepth.MaxDepth - WaterDepth.DeepFrom), 0f, 1f);
        float strength = 0.25f + (0.75f * Smooth(t));
        return new UnderwaterLook(
            strength,
            (byte)(70 + (130 * strength)),
            0.62f - (0.40f * strength),
            (byte)(170 + (80 * strength)),
            (byte)(70 * (1f - strength)),
            true);
    }

    private static float Smooth(float t) => t * t * (3f - (2f * t));
}
