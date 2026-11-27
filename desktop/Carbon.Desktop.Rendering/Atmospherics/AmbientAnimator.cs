// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Desktop.Rendering.World;

namespace Carbon.Desktop.Rendering.Atmospherics;

/// <summary>
/// Per-frame animated overlays drawn on top of the static, baked terrain meshes.
/// </summary>
internal static class AmbientAnimator
{
    private const float FaceHeight = 14f;

    /// <summary>
    /// Falling water on every waterfall curtain: droplets sliding down the face, a shimmering foam lip,
    /// and splash rings spreading at the foot.
    /// </summary>
    public static void DrawWaterfalls(GeometryBatch batch, ReadOnlySpan<ChunkTerrainRenderer.WaterfallEdge> edges, float time)
    {
        for (int e = 0; e < edges.Length; e++)
        {
            ChunkTerrainRenderer.WaterfallEdge edge = edges[e];
            Vector2 dir = edge.B - edge.A;
            uint seed = (uint)edge.Seed;

            // Streaks sliding down the curtain, each on its own phase
            for (int i = 0; i < 6; i++)
            {
                float jitter = Hash01(seed, i) - 0.5f;
                float t = ((i + 0.5f) / 6f) + (jitter * 0.1f);
                float phase = Frac((time * 1.9f) + Hash01(seed, i + 10));
                Vector2 p = edge.A + (dir * t) + new Vector2(0f, 1f + (phase * (FaceHeight - 3f)));
                byte alpha = (byte)(230f * (1f - (phase * 0.7f)));
                batch.FillRectangle(new RectangleF(p.X - 1f, p.Y, 2f, 3f + (phase * 2f)), new ColorRgba(235, 250, 255, alpha));
            }

            // Foam lip shimmer: bubbles drifting along the edge
            for (int i = 0; i < 3; i++)
            {
                float t = Frac((time * 0.35f) + (i / 3f) + Hash01(seed, i + 20));
                Vector2 p = edge.A + (dir * t);
                float pulse = 0.5f + (0.5f * MathF.Sin((time * 5f) + i));
                batch.FillRectangle(new RectangleF(p.X - 2f, p.Y - 2f, 4f, 2.5f), new ColorRgba(255, 255, 255, (byte)(140 + (pulse * 100f))));
            }

            // Splash rings at the foot of the fall
            for (int i = 0; i < 2; i++)
            {
                float phase = Frac((time * 1.1f) + (i * 0.5f) + Hash01(seed, 30));
                Vector2 foot = edge.A + (dir * (0.3f + (0.4f * Hash01(seed, 31 + i)))) + new Vector2(0f, FaceHeight + 3f);
                float w = 6f + (phase * 16f);
                batch.DrawRectangle(new RectangleF(foot.X - (w * 0.5f), foot.Y - (phase * 1.5f), w, 2f + (phase * 3f)), new ColorRgba(255, 255, 255, (byte)(170f * (1f - phase))), 1f);
            }
        }
    }

    /// <summary>
    /// Ripples on open water: pairs of light glints that swell, drift with the current and fade, each on
    /// its own phase so the surface twinkles instead of pulsing in unison.
    /// </summary>
    public static void DrawRipples(GeometryBatch batch, ReadOnlySpan<ChunkTerrainRenderer.RippleSpot> spots, float time, float tileSize)
    {
        for (int i = 0; i < spots.Length; i++)
        {
            ChunkTerrainRenderer.RippleSpot spot = spots[i];
            uint seed = (uint)spot.Seed;
            if (spot.IsShore)
            {
                DrawShoreFoam(batch, in spot, time, tileSize);
                continue;
            }

            if (!spot.IsDeep)
            {
                DrawCaustic(batch, in spot, time, tileSize);
            }

            float phase = Frac((time * (0.22f + (Hash01(seed, 1) * 0.12f))) + Hash01(seed, 2));
            float life = MathF.Sin(phase * MathF.PI);
            if (life < 0.05f)
            {
                continue;
            }

            float drift = (phase - 0.5f) * 10f;
            Vector2 c = spot.Centre + new Vector2(((Hash01(seed, 3) - 0.5f) * 18f) + drift, (Hash01(seed, 4) - 0.5f) * 18f);
            float w = 6f + (life * 8f);
            byte a = (byte)(life * (spot.IsDeep ? 70f : 120f));
            ColorRgba glint = spot.IsDeep ? new ColorRgba(150, 190, 230, a) : new ColorRgba(230, 248, 255, a);
            batch.FillRectangle(new RectangleF(c.X - (w * 0.5f), c.Y, w, 1.5f), glint);
            batch.FillRectangle(new RectangleF(c.X - (w * 0.3f) + 4f, c.Y + 4f, w * 0.6f, 1.5f), new ColorRgba(glint.R, glint.G, glint.B, (byte)(a * 0.6f)));
        }
    }

    /// <summary>
    /// Gets the lapping wave on a shoreline tile at <paramref name="time"/>: how far the crest has rolled toward the
    /// shore (0 = tile centre, 1 = the waterline) and how bright the foam is. A wave rolls in over the first 60 % of
    /// its cycle, then its wash fades at the waterline; neighbouring tiles are out of step.
    /// </summary>
    /// <param name="seed">The tile's seed.</param>
    /// <param name="time">Animation time in seconds.</param>
    /// <returns>The crest position and the foam alpha, 0..1.</returns>
    public static (float Crest, float Alpha) ShoreWave(uint seed, float time)
    {
        float period = 3.2f + (Hash01(seed, 5) * 1.2f);
        float phase = Frac((time / period) + Hash01(seed, 6));
        if (phase < 0.6f)
        {
            float t = phase / 0.6f;
            float crest = 1f - ((1f - t) * (1f - t)); // eases in as it reaches the shore
            return (crest, 0.35f + (0.65f * t));
        }

        return (1f, 1f - ((phase - 0.6f) / 0.4f));
    }

    // A foam crest rolling in along the tile toward the shore, then a fading wash line at the waterline.
    private static void DrawShoreFoam(GeometryBatch batch, in ChunkTerrainRenderer.RippleSpot spot, float time, float tileSize)
    {
        uint seed = (uint)spot.Seed;
        (float crest, float alpha) = ShoreWave(seed, time);
        Vector2 across = new(-spot.ToShore.Y, spot.ToShore.X);
        float half = tileSize * 0.55f;
        Vector2 line = spot.Centre + (spot.ToShore * (crest * tileSize * 0.46f));
        float wobble = MathF.Sin((time * 2.1f) + Hash01(seed, 7)) * 1.2f;
        Vector2 a = line - (across * half) + (spot.ToShore * wobble);
        Vector2 b = line + (across * half) - (spot.ToShore * wobble);
        batch.DrawLine(a, b, new ColorRgba(240, 252, 255, (byte)(190f * alpha)), 2.2f);

        // Bubbly trail a little behind the crest.
        Vector2 trail = line - (spot.ToShore * 3.5f);
        batch.DrawLine(trail - (across * half * 0.7f), trail + (across * half * 0.6f), new ColorRgba(220, 244, 250, (byte)(90f * alpha)), 1.3f);
    }

    // Shallow water: a faint, slowly drifting web of light on the bottom.
    private static void DrawCaustic(GeometryBatch batch, in ChunkTerrainRenderer.RippleSpot spot, float time, float tileSize)
    {
        uint seed = (uint)spot.Seed;
        float r = tileSize * 0.42f;
        for (int k = 0; k < 3; k++)
        {
            float a0 = (time * (0.35f + (k * 0.11f))) + (Hash01(seed, 40 + k) * MathF.Tau);
            Vector2 p0 = spot.Centre + new Vector2(MathF.Cos(a0) * r, MathF.Sin(a0 * 1.3f) * r * 0.6f);
            Vector2 p1 = spot.Centre + new Vector2(MathF.Cos(a0 + 2.1f) * r * 0.8f, MathF.Sin((a0 * 0.9f) + 2.1f) * r * 0.7f);
            float pulse = 0.5f + (0.5f * MathF.Sin((time * 1.7f) + k + Hash01(seed, 50)));
            batch.DrawLine(p0, p1, new ColorRgba(225, 250, 255, (byte)(28f + (36f * pulse))), 1.1f);
        }
    }

    /// <summary>Fractional part.</summary>
    public static float Frac(float v) => v - MathF.Floor(v);

    /// <summary>Stable pseudo-random value in [0, 1] for a seed and an index.</summary>
    public static float Hash01(uint seed, int i)
    {
        uint h = seed ^ (uint)(i * 0x9E3779B9);
        h ^= h >> 16;
        h *= 0x7FEB352D;
        h ^= h >> 15;
        h *= 0x846CA68B;
        h ^= h >> 16;
        return (h & 0xFFFF) / 65535f;
    }
}
