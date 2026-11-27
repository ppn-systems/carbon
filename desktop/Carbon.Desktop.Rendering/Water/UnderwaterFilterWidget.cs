// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;

namespace Carbon.Desktop.Rendering.Water;

/// <summary>
/// The underwater post-filter (ADR-032 D13), drawn over the world and under the HUD: a blue-green tint that darkens
/// with depth, a fog closing in from the screen edge around a clear circle of view, drifting light caustics, and
/// rising bubbles while diving. Geometry only; allocation-free.
/// </summary>
internal static class UnderwaterFilterWidget
{
    private const int FogRings = 14;
    private const int RingSegments = 48;
    private const int Caustics = 7;
    private const int Bubbles = 10;

    // Shallow, sunlit water and the dark abyss the tint blends between.
    private static readonly Vector3 s_shallowTint = new(18f, 96f, 112f);
    private static readonly Vector3 s_abyssTint = new(2f, 10f, 26f);

    /// <summary>Draws the filter for <paramref name="look"/>; nothing on land or while wading.</summary>
    /// <param name="batch">The screen-space geometry batch.</param>
    /// <param name="look">The underwater look.</param>
    /// <param name="viewportWidth">Viewport width in pixels.</param>
    /// <param name="viewportHeight">Viewport height in pixels.</param>
    /// <param name="time">Animation time in seconds.</param>
    public static void DrawGeometry(GeometryBatch batch, in UnderwaterLook look, int viewportWidth, int viewportHeight, float time)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (!look.IsVisible || viewportWidth <= 0 || viewportHeight <= 0)
        {
            return;
        }

        Vector3 tint = Vector3.Lerp(s_shallowTint, s_abyssTint, look.Strength);
        batch.FillRectangle(new RectangleF(0f, 0f, viewportWidth, viewportHeight), ToColor(tint, look.TintAlpha));

        Vector2 centre = new(viewportWidth * 0.5f, viewportHeight * 0.5f);
        float halfDiagonal = MathF.Sqrt((centre.X * centre.X) + (centre.Y * centre.Y));
        if (look.CausticsAlpha > 0)
        {
            DrawCaustics(batch, look.CausticsAlpha, viewportWidth, viewportHeight, time);
        }

        if (look.EdgeAlpha > 0)
        {
            DrawFog(batch, centre, look.ClearRadiusFraction * halfDiagonal, halfDiagonal + 2f, ToColor(tint * 0.55f, 0), look.EdgeAlpha);
        }

        if (look.IsSubmerged)
        {
            DrawBubbles(batch, centre, viewportHeight, time);
        }
    }

    // Concentric rings from the clear radius out past the corners, each a little more opaque: a soft fog edge.
    private static void DrawFog(GeometryBatch batch, Vector2 centre, float clearRadius, float outerRadius, ColorRgba fog, byte edgeAlpha)
    {
        float step = (outerRadius - clearRadius) / FogRings;
        for (int ring = 0; ring < FogRings; ring++)
        {
            float r0 = clearRadius + (ring * step);
            float r1 = ring == FogRings - 1 ? outerRadius * 1.5f : r0 + step;
            float k = (ring + 1f) / FogRings;
            byte alpha = (byte)(edgeAlpha * k * k * (3f - (2f * k)));
            ColorRgba color = new(fog.R, fog.G, fog.B, alpha);
            for (int s = 0; s < RingSegments; s++)
            {
                float a0 = s * MathF.Tau / RingSegments;
                float a1 = (s + 1) * MathF.Tau / RingSegments;
                Vector2 d0 = new(MathF.Cos(a0), MathF.Sin(a0));
                Vector2 d1 = new(MathF.Cos(a1), MathF.Sin(a1));
                Vector2 inner0 = centre + (d0 * r0);
                Vector2 inner1 = centre + (d1 * r0);
                Vector2 outer0 = centre + (d0 * r1);
                Vector2 outer1 = centre + (d1 * r1);
                batch.FillTriangle(inner0, outer0, outer1, color);
                batch.FillTriangle(inner0, outer1, inner1, color);
            }
        }
    }

    // Soft patches of light wandering across the water, each on its own slow Lissajous path.
    private static void DrawCaustics(GeometryBatch batch, byte alpha, int width, int height, float time)
    {
        for (int i = 0; i < Caustics; i++)
        {
            float phase = i * 1.7f;
            float x = width * (0.5f + (0.42f * MathF.Sin((time * 0.11f) + phase)));
            float y = height * (0.5f + (0.40f * MathF.Sin((time * 0.083f) + (phase * 1.3f))));
            float radius = 36f + (22f * (0.5f + (0.5f * MathF.Sin((time * 0.7f) + phase))));
            // A faint pool of light with a brighter rippling rim, like sunlight focused by the waves.
            batch.FillCircle(new Vector2(x, y), radius, new ColorRgba(190, 245, 255, (byte)(alpha * 0.14f)), 20);
            batch.DrawCircle(new Vector2(x, y), radius * (0.8f + (0.08f * MathF.Sin((time * 1.9f) + phase))), new ColorRgba(225, 252, 255, (byte)(alpha * 0.3f)), 24, 1.4f);
        }
    }

    // Bubbles rising from the diver at the centre of the view, wobbling and fading as they climb.
    private static void DrawBubbles(GeometryBatch batch, Vector2 centre, int viewportHeight, float time)
    {
        float climb = viewportHeight * 0.32f;
        for (int i = 0; i < Bubbles; i++)
        {
            float t = Frac((time * 0.45f) + (i * 0.137f));
            float wobble = MathF.Sin((time * 4f) + (i * 2.3f)) * (4f + (6f * t));
            Vector2 p = centre + new Vector2((((i % 3) - 1) * 6f) + wobble, -12f - (t * climb));
            float radius = 1.6f + (i % 4 * 0.8f) + (t * 1.5f);
            byte alpha = (byte)(170 * (1f - t));
            batch.DrawCircle(p, radius, new ColorRgba(225, 250, 255, alpha), 10, 1.1f);
            batch.FillCircle(p + new Vector2(-radius * 0.35f, -radius * 0.35f), radius * 0.3f, new ColorRgba(255, 255, 255, alpha), 6);
        }
    }

    private static ColorRgba ToColor(Vector3 c, byte alpha) =>
        new((byte)Math.Clamp(c.X, 0f, 255f), (byte)Math.Clamp(c.Y, 0f, 255f), (byte)Math.Clamp(c.Z, 0f, 255f), alpha);

    private static float Frac(float v) => v - MathF.Floor(v);
}
