// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;

namespace Carbon.Desktop.Rendering;

/// <summary>
/// Procedural mushrooms with varied cap shapes, stems and colours, used for mushroom patches and the
/// glowing ring around the World Tree.
/// </summary>
internal static class MushroomArt
{
    /// <summary>Cap shapes.</summary>
    public enum Shape : byte
    {
        /// <summary>Round dome (porcini, button).</summary>
        Dome,

        /// <summary>Wide, flat parasol on a tall stem.</summary>
        Parasol,

        /// <summary>Pointed cone (liberty cap, morel-like).</summary>
        Cone,

        /// <summary>Bell with a flared rim.</summary>
        Bell,

        /// <summary>Red dome with white warts (fly agaric).</summary>
        Spotted,

        /// <summary>Translucent glowing cap.</summary>
        Glowing,
    }

    private static readonly (ColorRgba Cap, ColorRgba Stem)[] s_palettes =
    [
        (new ColorRgba(150, 92, 52), new ColorRgba(236, 222, 196)),   // brown
        (new ColorRgba(214, 168, 104), new ColorRgba(244, 236, 214)), // tan
        (new ColorRgba(120, 74, 44), new ColorRgba(214, 196, 166)),   // dark brown
        (new ColorRgba(204, 120, 60), new ColorRgba(240, 226, 200)),  // orange chanterelle
        (new ColorRgba(150, 110, 190), new ColorRgba(226, 214, 236)), // violet
        (new ColorRgba(236, 228, 212), new ColorRgba(246, 242, 232)), // white
    ];

    /// <summary>Draws a natural cluster of 2-5 mushrooms around <paramref name="pos"/>.</summary>
    public static void DrawCluster(GeometryBatch batch, Vector2 pos, float size, uint seed)
    {
        int count = 2 + (int)(Hash01(seed, 2) * 4f);
        Shape shape = PickShape(seed);

        // Same species in a cluster, sizes vary: one big, the others smaller around it.
        FillEllipse(batch, pos + new Vector2(2f, 5f), size * 1.1f, size * 0.4f, new ColorRgba(0, 0, 0, 40), 16);
        Span<Vector2> spots = stackalloc Vector2[5];
        Span<float> scales = stackalloc float[5];
        for (int i = 0; i < count; i++)
        {
            float a = Hash01(seed, 10 + i) * MathF.Tau;
            float d = i == 0 ? 0f : size * (0.45f + (Hash01(seed, 20 + i) * 0.5f));
            spots[i] = pos + new Vector2(MathF.Cos(a) * d, MathF.Sin(a) * d * 0.45f);
            scales[i] = i == 0 ? 1f : 0.45f + (Hash01(seed, 30 + i) * 0.4f);
        }

        // Back to front
        for (int pass = 0; pass < count; pass++)
        {
            int best = -1;
            for (int i = 0; i < count; i++)
            {
                if (scales[i] > 0f && (best < 0 || spots[i].Y < spots[best].Y))
                {
                    best = i;
                }
            }

            DrawOne(batch, spots[best], size * scales[best], shape, seed + (uint)(best * 977), 0f);
            scales[best] = -1f;
        }
    }

    /// <summary>Draws one mushroom whose stem stands on <paramref name="foot"/>.</summary>
    public static void DrawOne(GeometryBatch batch, Vector2 foot, float size, Shape shape, uint seed, float time)
    {
        (ColorRgba cap, ColorRgba stem) = s_palettes[(int)(Hash01(seed, 3) * s_palettes.Length) % s_palettes.Length];
        if (shape == Shape.Spotted)
        {
            cap = new ColorRgba(206, 38, 38);
            stem = new ColorRgba(244, 238, 226);
        }
        else if (shape == Shape.Glowing)
        {
            cap = Hash01(seed, 4) > 0.5f ? new ColorRgba(110, 220, 230) : new ColorRgba(170, 140, 255);
            stem = new ColorRgba(200, 236, 240);
        }

        cap = Vary(cap, seed, 5, 0.12f);
        float stemH = size * (shape == Shape.Parasol ? 1.7f : 0.9f + (Hash01(seed, 6) * 0.5f));
        float stemW = size * (shape == Shape.Parasol ? 0.22f : 0.32f + (Hash01(seed, 7) * 0.1f));
        float lean = (Hash01(seed, 8) - 0.5f) * size * 0.5f;
        Vector2 top = foot + new Vector2(lean, -stemH);

        if (shape == Shape.Glowing)
        {
            float pulse = 0.5f + (0.5f * MathF.Sin((time * 2f) + (seed % 100)));
            batch.FillCircle(top, size * (1.6f + (pulse * 0.3f)), new ColorRgba(cap.R, cap.G, cap.B, (byte)(30 + (pulse * 30f))), 16);
        }

        // Stem: tapered, slightly bent, with a shaded right side and sometimes a ring
        Vector2 mid = Vector2.Lerp(foot, top, 0.5f) + new Vector2(-lean * 0.3f, 0f);
        Tapered(batch, foot, mid, stemW * 1.15f, stemW, stem);
        Tapered(batch, mid, top, stemW, stemW * 0.85f, stem);
        batch.DrawLine(foot + new Vector2(stemW * 0.35f, 0f), top + new Vector2(stemW * 0.3f, 0f), Shade(stem, 0.78f), MathF.Max(1f, stemW * 0.3f));
        if (shape is Shape.Parasol or Shape.Spotted || Hash01(seed, 9) > 0.7f)
        {
            FillEllipse(batch, Vector2.Lerp(foot, top, 0.72f), stemW * 0.9f, stemW * 0.35f, Shade(stem, 0.9f), 10);
        }

        float w = size * shape switch
        {
            Shape.Parasol => 1.45f,
            Shape.Cone => 0.65f,
            Shape.Bell => 0.8f,
            Shape.Dome or Shape.Spotted or Shape.Glowing => 0.95f + (Hash01(seed, 11) * 0.25f),
            _ => 0.95f + (Hash01(seed, 11) * 0.25f),
        };

        // Gills: the darker underside visible below the rim
        FillEllipse(batch, top + new Vector2(0f, 1f), w * 0.92f, w * 0.22f, Shade(cap, 0.55f), 16);

        switch (shape)
        {
            case Shape.Parasol:
                HalfEllipse(batch, top, w, w * 0.28f, cap);
                batch.FillCircle(top + new Vector2(0f, -w * 0.26f), w * 0.14f, Shade(cap, 0.8f), 8);
                break;
            case Shape.Cone:
                {
                    Span<Vector2> cone =
                    [
                        top + new Vector2(-w, 0f),
                    top + new Vector2(w, 0f),
                    top + new Vector2(w * 0.55f, -w * 0.9f),
                    top + new Vector2(w * 0.1f, -w * 1.55f),
                    top + new Vector2(-w * 0.2f, -w * 1.5f),
                    top + new Vector2(-w * 0.62f, -w * 0.85f),
                ];
                    batch.FillConvexPolygon(cone, cap);
                    break;
                }

            case Shape.Bell:
                HalfEllipse(batch, top + new Vector2(0f, -w * 0.15f), w * 0.8f, w * 1.1f, cap);
                FillEllipse(batch, top, w, w * 0.22f, cap, 14); // flared rim
                break;
            case Shape.Dome:
            case Shape.Spotted:
            case Shape.Glowing:
            default:
                HalfEllipse(batch, top, w, w * 0.72f, cap);
                break;
        }

        // Highlight on the upper-left of the cap
        float capH = shape switch
        {
            Shape.Parasol => w * 0.28f,
            Shape.Cone => w * 1.4f,
            Shape.Bell => w * 1.2f,
            Shape.Dome or Shape.Spotted or Shape.Glowing => w * 0.72f,
            _ => w * 0.72f,
        };
        FillEllipse(batch, top + new Vector2(-w * 0.35f, -capH * 0.55f), w * 0.28f, capH * 0.2f, new ColorRgba(255, 255, 255, 90), 10);

        if (shape == Shape.Spotted)
        {
            int spots = 4 + (int)(Hash01(seed, 12) * 4f);
            for (int i = 0; i < spots; i++)
            {
                float a = MathF.PI + (Hash01(seed, 40 + i) * MathF.PI);
                float d = 0.3f + (Hash01(seed, 50 + i) * 0.6f);
                Vector2 p = top + new Vector2(MathF.Cos(a) * w * d, MathF.Sin(a) * w * 0.72f * d);
                batch.FillCircle(p, MathF.Max(1.2f, w * (0.08f + (Hash01(seed, 60 + i) * 0.06f))), new ColorRgba(250, 246, 236), 6);
            }
        }
        else if (shape == Shape.Glowing)
        {
            FillEllipse(batch, top + new Vector2(0f, -capH * 0.4f), w * 0.5f, capH * 0.25f, new ColorRgba(255, 255, 255, 120), 10);
        }
    }

    /// <summary>Returns whether a mushroom patch with this seed is a glowing species.</summary>
    public static bool IsGlowing(uint seed) => PickShape(seed) == Shape.Glowing;

    private static Shape PickShape(uint seed)
    {
        float r = Hash01(seed, 1);
        return r switch
        {
            < 0.28f => Shape.Dome,
            < 0.44f => Shape.Parasol,
            < 0.60f => Shape.Cone,
            < 0.74f => Shape.Bell,
            < 0.90f => Shape.Spotted,
            _ => Shape.Glowing,
        };
    }

    private static void HalfEllipse(GeometryBatch batch, Vector2 c, float rx, float ry, ColorRgba color)
    {
        Span<Vector2> pts = stackalloc Vector2[13];
        for (int i = 0; i <= 12; i++)
        {
            float a = MathF.PI + (i * (MathF.PI / 12f));
            pts[i] = c + new Vector2(MathF.Cos(a) * rx, MathF.Sin(a) * ry);
        }

        batch.FillConvexPolygon(pts, color);
    }

    private static void Tapered(GeometryBatch batch, Vector2 a, Vector2 b, float wa, float wb, ColorRgba color)
    {
        Vector2 n = Vector2.Normalize(new Vector2(-(b.Y - a.Y), b.X - a.X));
        batch.FillTriangle(a - (n * wa * 0.5f), a + (n * wa * 0.5f), b + (n * wb * 0.5f), color);
        batch.FillTriangle(a - (n * wa * 0.5f), b + (n * wb * 0.5f), b - (n * wb * 0.5f), color);
    }

    internal static void FillEllipse(GeometryBatch batch, Vector2 c, float rx, float ry, ColorRgba color, int segments)
    {
        Vector2 prev = c + new Vector2(rx, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float a = i * (MathF.Tau / segments);
            Vector2 p = c + new Vector2(MathF.Cos(a) * rx, MathF.Sin(a) * ry);
            batch.FillTriangle(c, prev, p, color);
            prev = p;
        }
    }

    private static float Hash01(uint seed, int i) => AmbientAnimator.Hash01(seed, i);

    private static ColorRgba Vary(ColorRgba c, uint h, int i, float amount)
    {
        float k = 1f + ((Hash01(h, i) - 0.5f) * 2f * amount);
        return new ColorRgba((byte)Math.Clamp(c.R * k, 0f, 255f), (byte)Math.Clamp(c.G * k, 0f, 255f), (byte)Math.Clamp(c.B * k, 0f, 255f), c.A);
    }

    private static ColorRgba Shade(ColorRgba c, float k) =>
        new((byte)Math.Clamp(c.R * k, 0f, 255f), (byte)Math.Clamp(c.G * k, 0f, 255f), (byte)Math.Clamp(c.B * k, 0f, 255f), c.A);
}
