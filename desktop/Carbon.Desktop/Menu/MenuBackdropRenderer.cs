// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;

namespace Carbon.Desktop.Menu;

/// <summary>
/// Vector layers of the menu backdrop: the night sky with a treeline (shown while the world is generating, or for good if
/// it cannot be) and the fireflies drawn over everything. Screen space, no allocation.
/// </summary>
internal static class MenuBackdropRenderer
{
    /// <summary>Draws the sky gradient, stars and ridge silhouettes at <paramref name="alpha"/> (1 = opaque).</summary>
    public static void DrawSky(GeometryBatch batch, int viewportWidth, int viewportHeight, float elapsed, float alpha)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (viewportWidth <= 0 || viewportHeight <= 0 || alpha <= 0f)
        {
            return;
        }

        const int bands = 8;
        float bandH = MathF.Ceiling(viewportHeight / (float)bands);
        for (int i = 0; i < bands; i++)
        {
            float t = i / (float)(bands - 1);
            ColorRgba band = new((byte)(10 + (t * 16)), (byte)(16 + (t * 28)), (byte)(36 + (t * 32)), 255);
            batch.FillRectangle(new RectangleF(0f, i * bandH, viewportWidth, bandH + 1f), MenuPalette.WithAlpha(band, alpha));
        }

        for (int i = 0; i < 40; i++)
        {
            float sx = i * 397 % 1009 / 1009f * viewportWidth;
            float sy = i * 211 % 677 / 677f * viewportHeight * 0.6f;
            float twinkle = 0.45f + (0.35f * MathF.Sin((elapsed * 1.3f) + (i * 1.7f)));
            float size = i % 7 == 0 ? 2f : 1f;
            batch.FillRectangle(new RectangleF(sx, sy, size, size), MenuPalette.WithAlpha(new ColorRgba(214, 226, 255, 255), twinkle * alpha));
        }

        // Far ridge (slow) and near treeline (faster): whole-pixel parallax steps.
        DrawRidge(batch, viewportWidth, viewportHeight, elapsed * 2f, 0.70f, 70f, 11, new ColorRgba(20, 32, 52, 255), alpha);
        DrawRidge(batch, viewportWidth, viewportHeight, elapsed * 5f, 0.80f, 96f, 18, new ColorRgba(12, 22, 34, 255), alpha);
        batch.FillRectangle(new RectangleF(0f, viewportHeight - 54f, viewportWidth, 54f), MenuPalette.WithAlpha(new ColorRgba(9, 17, 26, 255), alpha));
    }

    /// <summary>Draws slowly drifting fireflies over the whole backdrop.</summary>
    public static void DrawFireflies(GeometryBatch batch, int viewportWidth, int viewportHeight, float elapsed)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (viewportWidth <= 0 || viewportHeight <= 0)
        {
            return;
        }

        for (int i = 0; i < 22; i++)
        {
            float seedX = i * 197 % 997 / 997f;
            float seedY = i * 313 % 787 / 787f;
            float px = ((seedX * viewportWidth) + (elapsed * (4f + (i % 5 * 2f)))) % viewportWidth;
            float py = (viewportHeight * 0.25f) + (seedY * viewportHeight * 0.7f) + (MathF.Sin((elapsed * 0.9f) + i) * 6f);
            float glow = 0.35f + (0.45f * (0.5f + (0.5f * MathF.Sin((elapsed * 1.8f) + (i * 2.3f)))));
            ColorRgba color = i % 3 == 0 ? new ColorRgba(250, 214, 96, 255) : new ColorRgba(190, 240, 150, 255);
            Vector2 centre = new(px, py);
            batch.FillCircle(centre, 4f, MenuPalette.WithAlpha(color, glow * 0.18f), segments: 10);
            batch.FillRectangle(new RectangleF(px - 1f, py - 1f, 2f, 2f), MenuPalette.WithAlpha(color, glow));
        }
    }

    private static void DrawRidge(GeometryBatch batch, int width, int height, float scroll, float baseline, float amplitude, int steps, ColorRgba color, float alpha)
    {
        float segW = MathF.Ceiling(width / (float)(steps - 1));
        float offset = MathF.Floor(scroll % segW);
        float baseH = height * baseline;
        ColorRgba fill = MenuPalette.WithAlpha(color, alpha);
        for (int i = -1; i < steps; i++)
        {
            int k = i + (int)MathF.Floor(scroll / segW);
            float peak = 30f + (MathF.Abs(MathF.Sin((k * 0.85f) + 0.4f)) * amplitude);
            float x0 = (i * segW) - offset;
            batch.FillTriangle(new Vector2(x0 - 10f, height), new Vector2(x0 + (segW * 0.5f), baseH - peak), new Vector2(x0 + segW + 10f, height), fill);
        }
    }
}
