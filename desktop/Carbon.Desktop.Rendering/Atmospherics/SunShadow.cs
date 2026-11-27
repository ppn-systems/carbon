// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;

namespace Carbon.Desktop.Rendering.Atmospherics;

/// <summary>
/// Sun (and faint moon) shadows by time of day (docs/art/graphics-quality.md, shadows): the sun rises in the east, so
/// morning shadows stretch long to the west, shrink to a short pool under the object at noon and stretch east toward
/// sunset. At night a faint moon shadow falls the other way. Rain and overcast soften them. Shadows are soft
/// radial-gradient ellipses in the existing world batch: a few triangles each, no extra pass.
/// </summary>
internal static class SunShadow
{
    /// <summary>The sun shadow at a moment: where it points, how long it is per unit of height, and how dark.</summary>
    /// <param name="Direction">Unit vector the shadow falls along (screen space: +x east, +y south).</param>
    /// <param name="Length">Shadow length per world pixel of object height.</param>
    /// <param name="Opacity">0 (no shadow) to 1 (full noon shadow).</param>
    public readonly record struct State(Vector2 Direction, float Length, float Opacity);

    /// <summary>Gets the shadow for an hour of day and a rain intensity.</summary>
    /// <param name="hour">Hour of day (wrapped to 0..24).</param>
    /// <param name="rain">Rain intensity, 0..1.</param>
    /// <returns>The shadow state.</returns>
    public static State At(float hour, float rain)
    {
        float h = ((hour % 24f) + 24f) % 24f;
        bool day = h is >= 6f and <= 18f;

        // Progress across the sky: 0 at rise (east), 0.5 at the top, 1 at set (west). The moon follows the same arc
        // from 18 h to 6 h.
        float arc = day ? (h - 6f) / 12f : (h + 6f) % 24f / 12f;
        float angle = arc * MathF.PI;
        float elevation = MathF.Sin(angle);
        Vector2 direction = Vector2.Normalize(new Vector2(-MathF.Cos(angle), 0.45f));
        float length = 0.3f + (1.3f * (1f - elevation));

        // Shadows fade in after sunrise and out before sunset (no hard switch at 6 h and 18 h).
        float sunUp = Math.Clamp(elevation / 0.25f, 0f, 1f);
        float opacity = day ? sunUp : 0.28f * sunUp;
        opacity *= 1f - (0.75f * Math.Clamp(rain, 0f, 1f));
        return new State(direction, length, opacity);
    }

    /// <summary>
    /// Draws the soft shadow an object of <paramref name="height"/> casts from its foot, its base widened to
    /// <paramref name="radius"/>.
    /// </summary>
    /// <param name="batch">The world geometry batch.</param>
    /// <param name="state">The sun shadow state.</param>
    /// <param name="foot">Where the object meets the ground.</param>
    /// <param name="radius">Half the width of the object's base (a tree's canopy radius).</param>
    /// <param name="height">How tall the object is, in world pixels.</param>
    /// <param name="darkness">Peak alpha at full opacity, 0..255.</param>
    public static void Draw(GeometryBatch batch, in State state, Vector2 foot, float radius, float height, float darkness)
    {
        if (state.Opacity <= 0.02f)
        {
            return;
        }

        float reach = height * state.Length;
        Vector2 centre = foot + (state.Direction * (reach * 0.5f));
        byte alpha = (byte)Math.Clamp(darkness * state.Opacity, 0f, 255f);
        ColorRgba core = new(18, 22, 40, alpha);
        ColorRgba rim = new(18, 22, 40, 0);

        // Long shadows are wide ellipses along the sun's (mostly east-west) direction.
        float rx = radius + (reach * 0.5f * MathF.Abs(state.Direction.X));
        float ry = (radius * 0.45f) + (reach * 0.5f * state.Direction.Y);
        batch.FillRadialGradient(centre, rx, core, rim, 20, ry / rx);
    }
}
