// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;

namespace Carbon.Desktop.Rendering.Atmospherics;

/// <summary>
/// Clouds with a life cycle: each cloud forms puff by puff, grows, drifts with the wind and dissolves
/// puff by puff, then a new, different cloud forms in its place. Four kinds: fair-weather cumulus,
/// long stratus sheets, high cirrus wisps (faster, higher) and dark storm clouds when it rains.
/// Lighting follows the sun: lit tops, shaded bases, warm tints at dawn and dusk, dim blue at night,
/// and ground shadows cast away from the sun.
/// </summary>
internal sealed class CloudSystem
{
    private const float LowCell = 820f;
    private const float HighCell = 1100f;
    private const int MaxPuffs = 18;

    // Cirrus emits two puffs per step, so a cloud yields at most twice MaxPuffs.
    private const int MaxPuffSlots = MaxPuffs * 2;

    private static readonly Vector2 s_wind = new(15f, 4.5f);

    private readonly List<Cloud> _visible = new(64);

    /// <summary>Kinds of cloud.</summary>
    public enum Kind : byte
    {
        Cumulus,
        Stratus,
        Cirrus,
        Storm,
    }

    private readonly record struct Cloud(Vector2 Centre, float Size, Kind Kind, uint Seed, float Life, float Altitude);

    private readonly record struct Puff(Vector2 Position, float Radius, float Life);

    /// <summary>Gets how many clouds the last <see cref="Update"/> found around the view.</summary>
    internal int VisibleCount => _visible.Count;

    /// <summary>Gets the centre of visible cloud <paramref name="index"/>.</summary>
    internal Vector2 VisibleCentre(int index) => _visible[index].Centre;

    /// <summary>
    /// Works out which clouds are alive in and around the view this frame. Call once per frame before
    /// drawing shadows and bodies.
    /// </summary>
    public void Update(float time, float rain, float minX, float minY, float maxX, float maxY)
    {
        _visible.Clear();
        float coverage = 0.38f + (rain * 0.55f);
        this.CollectLayer(time, rain, coverage, minX, minY, maxX, maxY, high: false);
        this.CollectLayer(time, rain, 0.35f * (1f - rain), minX, minY, maxX, maxY, high: true);
    }

    /// <summary>Soft shadows on the ground, cast away from the sun (draw under props).</summary>
    public void DrawShadows(GeometryBatch batch, float hour, float night, float rain)
    {
        float daylight = 1f - night;
        if (daylight < 0.05f)
        {
            return;
        }

        Vector2 sun = SunShadowDirection(hour);
        Span<Puff> puffs = stackalloc Puff[MaxPuffSlots];
        foreach (Cloud cloud in _visible)
        {
            if (cloud.Kind == Kind.Cirrus)
            {
                continue; // too thin to cast a visible shadow
            }

            Vector2 offset = sun * cloud.Altitude;
            float strength = (cloud.Kind == Kind.Storm ? 34f : cloud.Kind == Kind.Stratus ? 16f : 24f) * daylight * (0.8f + (rain * 0.4f));
            int count = CollectPuffs(cloud, puffs);
            for (int i = 0; i < count; i++)
            {
                (Vector2 p, float r, float puffLife) = puffs[i];
                float a = strength * cloud.Life * puffLife;
                FillEllipse(batch, p + offset, r * 1.05f, r * 0.62f, new ColorRgba(12, 22, 38, (byte)(a * 0.55f)), 20);
                FillEllipse(batch, p + offset, r * 0.75f, r * 0.44f, new ColorRgba(12, 22, 38, (byte)(a * 0.45f)), 18);
            }
        }
    }

    /// <summary>Cloud bodies above everything.</summary>
    public void DrawClouds(GeometryBatch batch, float hour, float night, float rain, Vector2 player)
    {
        (ColorRgba lit, ColorRgba mid, ColorRgba shade) = Palette(hour, night, rain);
        Span<Puff> puffs = stackalloc Puff[MaxPuffSlots];
        foreach (Cloud cloud in _visible)
        {
            // Clouds thin out where they pass right over the player, so they never hide the action.
            float overPlayer = Math.Clamp(Vector2.Distance(cloud.Centre, player) / (cloud.Size * 1.2f), 0.35f, 1f);
            float baseAlpha = cloud.Kind switch
            {
                Kind.Cirrus => 70f,
                Kind.Stratus => 80f,
                Kind.Storm => 150f,
                Kind.Cumulus => 120f,
                _ => 120f,
            } * cloud.Life * overPlayer * (1f - (0.45f * night));

            ColorRgba cLit = cloud.Kind == Kind.Storm ? Mix(mid, shade, 0.55f) : lit;
            ColorRgba cMid = cloud.Kind == Kind.Storm ? shade : mid;
            ColorRgba cShade = cloud.Kind == Kind.Storm ? Mix(shade, new ColorRgba(30, 34, 44), 0.6f) : shade;

            // Three passes give each cloud volume: shaded base, body, sunlit crown.
            int count = CollectPuffs(cloud, puffs);
            for (int i = 0; i < count; i++)
            {
                (Vector2 p, float r, float puffLife) = puffs[i];
                float a = baseAlpha * puffLife;
                FillEllipse(batch, p + new Vector2(0f, r * 0.22f), r * 1.02f, r * 0.82f, WithAlpha(cShade, a * 0.45f), 22);
            }

            for (int i = 0; i < count; i++)
            {
                (Vector2 p, float r, float puffLife) = puffs[i];
                float a = baseAlpha * puffLife;
                FillEllipse(batch, p, r, r * 0.86f, WithAlpha(cMid, a * 0.40f), 22);
                FillEllipse(batch, p, r * 0.72f, r * 0.62f, WithAlpha(cMid, a * 0.30f), 18);
            }

            for (int i = 0; i < count; i++)
            {
                (Vector2 p, float r, float puffLife) = puffs[i];
                float a = baseAlpha * puffLife;
                FillEllipse(batch, p + new Vector2(-r * 0.22f, -r * 0.26f), r * 0.6f, r * 0.48f, WithAlpha(cLit, a * 0.45f), 16);
            }
        }
    }

    // ── Life cycle ──────────────────────────────────────────────────────────

    private void CollectLayer(float time, float rain, float coverage, float minX, float minY, float maxX, float maxY, bool high)
    {
        float cell = high ? HighCell : LowCell;
        Vector2 drift = s_wind * time * (high ? 1.8f : 1f);
        float margin = cell;
        int cx0 = (int)MathF.Floor((minX - drift.X - margin) / cell);
        int cx1 = (int)MathF.Floor((maxX - drift.X + margin) / cell);
        int cy0 = (int)MathF.Floor((minY - drift.Y - margin) / cell);
        int cy1 = (int)MathF.Floor((maxY - drift.Y + margin) / cell);

        for (int cy = cy0; cy <= cy1; cy++)
        {
            for (int cx = cx0; cx <= cx1; cx++)
            {
                uint cellSeed = (uint)((cx * 73856093) ^ (cy * 19349663) ^ (high ? 0x5bd1e995 : 0));

                // Every cell runs its own cycle: a cloud is born, lives, dissolves, and the cell stays
                // clear for a while. The share of the cycle a cloud is alive follows the coverage, so
                // the sky fills up before rain and clears afterwards without clouds popping in or out.
                float period = 70f + (Hash01(cellSeed, 1) * 80f);
                float cyclePos = (time / period) + Hash01(cellSeed, 2);
                int generation = (int)MathF.Floor(cyclePos);
                float f = cyclePos - generation;
                float duty = Math.Clamp(coverage * (0.7f + (Hash01(cellSeed + (uint)generation, 3) * 0.6f)), 0f, 0.95f);
                if (f > duty || duty < 0.05f)
                {
                    continue;
                }

                float u = f / duty; // 0 = forming, 1 = gone
                float life = Smooth(Math.Clamp(u / 0.22f, 0f, 1f)) * Smooth(Math.Clamp((1f - u) / 0.28f, 0f, 1f));
                if (life < 0.01f)
                {
                    continue;
                }

                uint seed = cellSeed ^ ((uint)generation * 0x9E3779B9u); // each generation is a new cloud
                Kind kind = high ? Kind.Cirrus : PickKind(seed, rain);
                float size = kind switch
                {
                    Kind.Stratus => 260f + (Hash01(seed, 4) * 180f),
                    Kind.Storm => 230f + (Hash01(seed, 4) * 140f),
                    Kind.Cirrus => 220f + (Hash01(seed, 4) * 200f),
                    Kind.Cumulus => 120f + (Hash01(seed, 4) * 120f),
                    _ => 120f + (Hash01(seed, 4) * 120f),
                };

                // Clouds swell a little as they form and while they live
                size *= 0.7f + (0.3f * Smooth(Math.Clamp(u / 0.5f, 0f, 1f)));
                Vector2 centre = new Vector2((cx + 0.2f + (Hash01(seed, 5) * 0.6f)) * cell, (cy + 0.2f + (Hash01(seed, 6) * 0.6f)) * cell) + drift;
                if (centre.X + (size * 1.6f) < minX || centre.X - (size * 1.6f) > maxX + 400f ||
                    centre.Y + (size * 1.2f) < minY - 300f || centre.Y - (size * 1.2f) > maxY + 300f)
                {
                    continue;
                }

                float altitude = high ? 420f : kind == Kind.Storm ? 180f : 260f;
                _visible.Add(new Cloud(centre, size, kind, seed, life, altitude));
            }
        }
    }

    private static Kind PickKind(uint seed, float rain)
    {
        float r = Hash01(seed, 7);
        if (rain > 0.35f)
        {
            return r < 0.55f ? Kind.Storm : r < 0.85f ? Kind.Stratus : Kind.Cumulus;
        }

        return r < 0.68f ? Kind.Cumulus : Kind.Stratus;
    }

    // Puffs of a cloud in its own shape; each puff has its own life so clouds build up and dissolve
    // gradually instead of fading as one block. Written into a caller's stack buffer (at most
    // MaxPuffSlots) instead of invoking a per-frame closure for each draw pass (audit round 2, PERF-16).
    private static int CollectPuffs(in Cloud cloud, Span<Puff> puffs)
    {
        int written = 0;
        int count = cloud.Kind switch
        {
            Kind.Stratus => 12,
            Kind.Cirrus => 9,
            Kind.Storm => MaxPuffs,
            Kind.Cumulus => 9 + (int)(Hash01(cloud.Seed, 8) * 7f),
            _ => 9 + (int)(Hash01(cloud.Seed, 8) * 7f),
        };

        for (int i = 0; i < count; i++)
        {
            float t = (i + 0.5f) / count;
            Vector2 p;
            float r;
            switch (cloud.Kind)
            {
                case Kind.Stratus:
                    // A long, flat sheet along the wind
                    p = new Vector2((t - 0.5f) * cloud.Size * 2.4f, (Hash01(cloud.Seed, 20 + i) - 0.5f) * cloud.Size * 0.25f);
                    r = cloud.Size * (0.28f + (Hash01(cloud.Seed, 40 + i) * 0.14f)) * (1f - (MathF.Abs(t - 0.5f) * 0.8f));
                    p.Y *= 0.6f;
                    break;
                case Kind.Cirrus:
                    {
                        // Thin streaks curving gently, swept along the wind
                        float bend = (Hash01(cloud.Seed, 9) - 0.5f) * 0.6f;
                        float x = (t - 0.5f) * cloud.Size * 2.6f;
                        p = new Vector2(x, (bend * x * x / cloud.Size) + ((Hash01(cloud.Seed, 20 + i) - 0.5f) * cloud.Size * 0.12f));
                        r = cloud.Size * (0.10f + (Hash01(cloud.Seed, 40 + i) * 0.08f));
                        break;
                    }

                case Kind.Cumulus:
                case Kind.Storm:
                default:
                    {
                        // A dome: flat base along the bottom, bigger towers in the middle
                        float a = Hash01(cloud.Seed, 20 + i) * MathF.Tau;
                        float d = MathF.Sqrt(Hash01(cloud.Seed, 30 + i));
                        float x = MathF.Cos(a) * d * cloud.Size * 0.85f;
                        float y = -MathF.Abs(MathF.Sin(a)) * d * cloud.Size * 0.45f;
                        float tower = 1f - (MathF.Abs(x) / (cloud.Size * 0.9f));
                        p = new Vector2(x, y - (tower * cloud.Size * (cloud.Kind == Kind.Storm ? 0.35f : 0.2f)));
                        r = cloud.Size * (0.26f + (Hash01(cloud.Seed, 40 + i) * 0.16f)) * (0.7f + (tower * 0.4f));
                        break;
                    }
            }

            // Each puff appears and fades at its own moment within the cloud's life
            float threshold = Hash01(cloud.Seed, 60 + i) * 0.6f;
            float puffLife = Math.Clamp((cloud.Life - threshold) / 0.4f, 0f, 1f);
            if (puffLife <= 0f)
            {
                continue;
            }

            if (cloud.Kind == Kind.Cirrus)
            {
                // Stretch along the wind
                puffs[written++] = new Puff(cloud.Centre + p, r, puffLife);
                puffs[written++] = new Puff(cloud.Centre + p + new Vector2(r * 1.4f, r * 0.2f), r * 0.8f, puffLife * 0.8f);
                continue;
            }

            puffs[written++] = new Puff(cloud.Centre + p, r * (0.8f + (0.2f * puffLife)), Smooth(puffLife));
        }

        return written;
    }

    // ── Light ───────────────────────────────────────────────────────────────

    // Direction shadows fall: away from the sun, which rises in the east and sets in the west.
    private static Vector2 SunShadowDirection(float hour)
    {
        float t = Math.Clamp((hour - 6f) / 12f, 0f, 1f); // 0 = sunrise, 1 = sunset
        float x = -1f + (2f * t);                         // morning shadows to the west, evening to the east
        float length = 0.45f + (MathF.Abs(x) * 0.8f);   // long shadows near sunrise and sunset
        return new Vector2(x * length, 0.55f);
    }

    private static (ColorRgba Lit, ColorRgba Mid, ColorRgba Shade) Palette(float hour, float night, float rain)
    {
        ColorRgba lit = new(255, 255, 255);
        ColorRgba mid = new(240, 244, 250);
        ColorRgba shade = new(188, 198, 214);

        // Warm light at dawn and dusk
        float warm = MathF.Max(Bell(hour, 6.6f, 1.3f), Bell(hour, 18.4f, 1.3f));
        lit = Mix(lit, new ColorRgba(255, 196, 160), warm);
        mid = Mix(mid, new ColorRgba(250, 214, 200), warm * 0.8f);
        shade = Mix(shade, new ColorRgba(170, 140, 170), warm * 0.7f);

        // Grey when it rains, blue-grey at night
        lit = Mix(lit, new ColorRgba(200, 206, 216), rain * 0.7f);
        mid = Mix(mid, new ColorRgba(170, 176, 188), rain * 0.7f);
        shade = Mix(shade, new ColorRgba(110, 116, 130), rain * 0.7f);
        lit = Mix(lit, new ColorRgba(120, 136, 170), night);
        mid = Mix(mid, new ColorRgba(90, 104, 140), night);
        shade = Mix(shade, new ColorRgba(50, 60, 90), night);
        return (lit, mid, shade);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static float Bell(float x, float centre, float width)
    {
        float d = (x - centre) / width;
        return MathF.Max(0f, 1f - (d * d));
    }

    private static float Smooth(float t) => t * t * (3f - (2f * t));

    private static float Hash01(uint seed, int i) => AmbientAnimator.Hash01(seed, i);

    private static ColorRgba WithAlpha(ColorRgba c, float a) => new(c.R, c.G, c.B, (byte)Math.Clamp(a, 0f, 255f));

    private static ColorRgba Mix(ColorRgba a, ColorRgba b, float t) => new(
        (byte)(a.R + ((b.R - a.R) * t)),
        (byte)(a.G + ((b.G - a.G) * t)),
        (byte)(a.B + ((b.B - a.B) * t)),
        (byte)(a.A + ((b.A - a.A) * t)));

    private static void FillEllipse(GeometryBatch batch, Vector2 center, float rx, float ry, ColorRgba color, int segments)
    {
        Vector2 prev = center + new Vector2(rx, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float a = i * (MathF.Tau / segments);
            Vector2 next = center + new Vector2(MathF.Cos(a) * rx, MathF.Sin(a) * ry);
            batch.FillTriangle(center, prev, next, color);
            prev = next;
        }
    }
}
