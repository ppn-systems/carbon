// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;

namespace Carbon.Desktop.Rendering;

/// <summary>
/// Art for the World Tree: an organic, buttressed trunk with cylindrical shading and twisting bark,
/// curved roots hugging the ground, great limbs reaching into a dome of ~120 leaf clumps lit from the
/// top-left, a dappled shadow on the ground, and ambient life (light shafts, fireflies, falling leaves).
/// All coordinates are relative to the trunk's foot (its ground contact point at the front).
/// </summary>
internal static class WorldTreeArt
{
    private const float TrunkHeight = 250f;
    private const float CanopyRise = 330f; // canopy centre above the foot
    private const float CanopyRx = 250f;
    private const float CanopyRy = 165f;

    private static readonly ColorRgba s_barkLight = new(146, 108, 74);
    private static readonly ColorRgba s_bark = new(104, 74, 50);
    private static readonly ColorRgba s_barkDark = new(64, 44, 30);
    private static readonly ColorRgba s_barkDeep = new(40, 27, 19);
    private static readonly ColorRgba s_moss = new(92, 140, 70);
    private static readonly ColorRgba s_spirit = new(140, 255, 214);

    // Leaf clumps: offset from the canopy centre, radius, and height tone (0 = underside, 1 = crown).
    private static readonly (Vector2 Offset, float Radius, float Tone)[] s_clumps = BuildClumps();

    /// <summary>Ground layer: dappled canopy shadow, moss, grass and flowers around the foot.</summary>
    public static void DrawGround(GeometryBatch batch, Vector2 foot, float radius, float time)
    {
        // Soft shadow of the crown, built up from layered low-alpha ellipses so it has no hard edge
        Vector2 sc = foot + new Vector2(30f, -60f);
        for (int i = 0; i < 6; i++)
        {
            float k = 1f - (i * 0.13f);
            FillEllipse(batch, sc, CanopyRx * 1.05f * k, 120f * k, new ColorRgba(10, 30, 14, 16), 40);
        }

        // Dapples of light breaking through the leaves
        for (int i = 0; i < 26; i++)
        {
            float a = Hash01(5u, i) * MathF.Tau;
            float d = MathF.Sqrt(Hash01(6u, i));
            Vector2 p = sc + new Vector2(MathF.Cos(a) * d * CanopyRx * 0.9f, MathF.Sin(a) * d * 100f);
            float flicker = 0.6f + (0.4f * MathF.Sin((time * 0.8f) + i));
            FillEllipse(batch, p, 10f + (Hash01(7u, i) * 14f), 5f + (Hash01(8u, i) * 6f), new ColorRgba(255, 244, 190, (byte)(22 * flicker)), 14);
        }

        // Moss patches and grass tufts
        for (int i = 0; i < 40; i++)
        {
            float a = Hash01(9u, i) * MathF.Tau;
            float d = 60f + (Hash01(10u, i) * radius * 0.55f);
            Vector2 p = foot + new Vector2(MathF.Cos(a) * d, MathF.Sin(a) * d * 0.55f);
            ColorRgba m = Mix(s_moss, new ColorRgba(70, 120, 60), Hash01(11u, i));
            FillEllipse(batch, p, 8f + (Hash01(12u, i) * 14f), 4f + (Hash01(13u, i) * 6f), new ColorRgba(m.R, m.G, m.B, 150), 12);
            if (i % 2 == 0)
            {
                for (int k = -1; k <= 1; k++)
                {
                    batch.DrawLine(p + new Vector2(k * 3f, 0f), p + new Vector2((k * 5f) + 1f, -7f - (Hash01(14u, i + k) * 4f)), new ColorRgba(110, 170, 80), 1.5f);
                }
            }
        }

        // Wildflowers in loose drifts
        for (int i = 0; i < 46; i++)
        {
            float a = Hash01(15u, i) * MathF.Tau;
            float d = 90f + (Hash01(16u, i) * radius * 0.6f);
            Vector2 p = foot + new Vector2(MathF.Cos(a) * d, MathF.Sin(a) * d * 0.55f);
            ColorRgba petal = (i % 4) switch
            {
                0 => new ColorRgba(253, 224, 71),
                1 => new ColorRgba(244, 160, 200),
                2 => new ColorRgba(196, 181, 253),
                _ => new ColorRgba(255, 255, 255),
            };
            batch.FillCircle(p, 2.6f, petal, 6);
            batch.FillCircle(p, 1f, new ColorRgba(250, 200, 60), 4);
        }
    }

    /// <summary>The trunk with its roots and great limbs (Y-sorted with entities).</summary>
    public static void DrawTrunk(GeometryBatch batch, Vector2 foot, float time)
    {
        // Contour: flared base, gentle S-curve, tapering to the crotch where the limbs split
        const int N = 14;
        Span<Vector2> left = stackalloc Vector2[N + 1];
        Span<Vector2> right = stackalloc Vector2[N + 1];
        for (int i = 0; i <= N; i++)
        {
            float t = i / (float)N;
            float y = foot.Y - (t * TrunkHeight);
            float half = 36f + (24f * (1f - t)) + (30f * MathF.Pow(1f - t, 4f));
            float x = foot.X + (MathF.Sin((t * 3.1f) + 0.4f) * 9f);
            left[i] = new Vector2(x - half, y);
            right[i] = new Vector2(x + half, y);
        }

        // Base shadow
        FillEllipse(batch, foot + new Vector2(14f, 4f), 120f, 22f, new ColorRgba(0, 0, 0, 70), 28);

        // Cylindrical shading: dark body, mid band, lit left band, deep right edge
        FillBand(batch, left, right, 0f, 1f, s_barkDark);
        FillBand(batch, left, right, 0.08f, 0.62f, s_bark);
        FillBand(batch, left, right, 0.12f, 0.34f, s_barkLight);
        FillBand(batch, left, right, 0.86f, 1f, s_barkDeep);

        // Twisting bark grooves
        for (int g = 0; g < 7; g++)
        {
            float f0 = 0.1f + (g * 0.13f);
            Vector2 prev = Lerp(left[0], right[0], f0);
            for (int i = 1; i <= N; i++)
            {
                float t = i / (float)N;
                float f = Math.Clamp(f0 + (MathF.Sin((t * 4f) + g) * 0.06f) + (t * 0.05f), 0.04f, 0.96f);
                Vector2 p = Lerp(left[i], right[i], f);
                batch.DrawLine(prev, p, g % 2 == 0 ? s_barkDeep : new ColorRgba(84, 60, 40), g % 3 == 0 ? 2.5f : 1.5f);
                prev = p;
            }
        }

        // Moss creeping up the shaded side, and a hollow glowing with spirit light
        for (int i = 0; i < 5; i++)
        {
            float t = 0.05f + (Hash01(21u, i) * 0.35f);
            int idx = Math.Clamp((int)(t * N), 0, N - 1);
            Vector2 p = Lerp(left[idx], right[idx], 0.6f + (Hash01(22u, i) * 0.25f));
            FillEllipse(batch, p, 4f + (Hash01(23u, i) * 3f), 12f + (Hash01(24u, i) * 10f), new ColorRgba(72, 104, 58, 150), 12);
        }

        float pulse = 0.5f + (0.5f * MathF.Sin(time * 1.4f));
        Vector2 hollow = Lerp(left[4], right[4], 0.42f);
        FillEllipse(batch, hollow, 30f + (pulse * 6f), 36f + (pulse * 6f), new ColorRgba(s_spirit.R, s_spirit.G, s_spirit.B, (byte)(25 + (pulse * 25f))), 20);
        FillEllipse(batch, hollow, 13f, 19f, s_barkDeep, 16);
        FillEllipse(batch, hollow + new Vector2(0f, 3f), 8f, 13f, new ColorRgba(s_spirit.R, s_spirit.G, s_spirit.B, (byte)(110 + (pulse * 100f))), 14);

        // Rune glyphs carved along the bark, faintly glowing
        for (int i = 0; i < 4; i++)
        {
            int idx = 6 + (i * 2);
            Vector2 p = Lerp(left[idx], right[idx], 0.3f + (i % 2 * 0.35f));
            ColorRgba glow = new(s_spirit.R, s_spirit.G, s_spirit.B, (byte)(70 + (pulse * 110f)));
            batch.DrawLine(p + new Vector2(-4f, -6f), p + new Vector2(0f, 6f), glow, 2f);
            batch.DrawLine(p + new Vector2(0f, 6f), p + new Vector2(4f, -6f), glow, 2f);
            batch.DrawLine(p + new Vector2(-3f, -1f), p + new Vector2(3f, -1f), glow, 1.5f);
        }

        DrawLimbs(batch, left[N], right[N]);

        // Roots last: they emerge from the flared base and crawl outwards over the ground in front of it.
        DrawRoots(batch, foot, time);
    }

    /// <summary>
    /// The crown, drawn above everything. While the player stands beneath it a window opens in the
    /// leaves around them instead of fading the whole crown.
    /// </summary>
    public static void DrawCanopy(GeometryBatch batch, Vector2 foot, float time, Vector2 player)
    {
        Vector2 c = foot + new Vector2(0f, -CanopyRise);
        float sway = MathF.Sin(time * 0.5f) * 2f;
        bool under = MathF.Abs(player.X - c.X) < CanopyRx + 30f && player.Y > c.Y - CanopyRy - 30f && player.Y < c.Y + CanopyRy + 40f;
        const float Window = 105f;

        ColorRgba deep = new(18, 70, 40);
        ColorRgba body = new(36, 118, 62);
        ColorRgba lit = new(92, 176, 84);
        ColorRgba crown = new(176, 222, 120);

        for (int i = 0; i < s_clumps.Length; i++)
        {
            (Vector2 off, float r, float tone) = s_clumps[i];
            Vector2 p = c + off + new Vector2(sway * (1f - tone), 0f);
            if (under)
            {
                float d = Vector2.Distance(p, player);
                if (d < Window)
                {
                    continue;
                }

                // Soften the window's edge: clumps near it are drawn smaller
                r *= Math.Clamp((d - Window) / 40f, 0.45f, 1f);
            }

            ColorRgba main = tone < 0.5f ? Mix(deep, body, tone * 2f) : Mix(body, lit, (tone - 0.5f) * 2f);
            batch.FillCircle(p + new Vector2(0f, r * 0.25f), r, Shade(main, 0.72f), 14);
            batch.FillCircle(p, r * 0.92f, main, 14);
            if (tone > 0.55f)
            {
                batch.FillCircle(p + new Vector2(-r * 0.3f, -r * 0.32f), r * 0.42f, Mix(lit, crown, tone), 10);
            }
        }

        // Luminous blossoms scattered over the crown, breathing slowly
        for (int i = 0; i < 28; i++)
        {
            (Vector2 off, float _, float tone) = s_clumps[i * 7 % s_clumps.Length];
            Vector2 p = c + off + new Vector2((Hash01(31u, i) - 0.5f) * 20f, (Hash01(32u, i) - 0.5f) * 14f);
            if (under && Vector2.Distance(p, player) < Window)
            {
                continue;
            }

            float b = 0.5f + (0.5f * MathF.Sin((time * 1.3f) + (i * 0.9f)));
            ColorRgba petal = i % 3 == 0 ? new ColorRgba(255, 236, 170) : new ColorRgba(190, 255, 230);
            batch.FillCircle(p, 7f + (b * 3f), new ColorRgba(petal.R, petal.G, petal.B, (byte)(30 + (b * 30f))), 10);
            batch.FillCircle(p, 2.5f + (tone * 1.5f), petal, 8);
        }

        // Soft light shafts: each is three nested slanted quads of decreasing alpha
        for (int i = 0; i < 4; i++)
        {
            float pulse = 0.5f + (0.5f * MathF.Sin((time * 0.6f) + (i * 1.9f)));
            float x = c.X - 160f + (i * 95f);
            for (int k = 0; k < 3; k++)
            {
                float w = 10f + (k * 9f);
                Vector2 a0 = new(x - w, c.Y + 40f);
                Vector2 a1 = new(x + w, c.Y + 40f);
                Vector2 b0 = new(x - w + 60f, foot.Y + 20f);
                Vector2 b1 = new(x + w + 70f, foot.Y + 20f);
                ColorRgba light = new(255, 246, 205, (byte)((10 + (pulse * 10f)) / (k + 1)));
                batch.FillTriangle(a0, a1, b1, light);
                batch.FillTriangle(a0, b1, b0, light);
            }
        }

        // Fireflies drifting beneath the crown
        for (int i = 0; i < 22; i++)
        {
            float t = time * (0.2f + (Hash01(41u, i) * 0.25f));
            Vector2 p = new(foot.X + (MathF.Sin(t + (i * 1.7f)) * CanopyRx * 0.9f), foot.Y - 60f + (MathF.Cos((t * 1.3f) + i) * 90f));
            float glow = 0.5f + (0.5f * MathF.Sin((time * 4f) + (i * 2.3f)));
            batch.FillCircle(p, 7f, new ColorRgba(253, 230, 138, (byte)(35 * glow)), 10);
            batch.FillCircle(p, 1.8f, new ColorRgba(255, 250, 200, (byte)(110 + (130 * glow))), 6);
        }

        // Leaves spiralling down
        for (int i = 0; i < 12; i++)
        {
            float phase = AmbientAnimator.Frac((time * 0.11f) + Hash01(43u, i));
            float lx = c.X - CanopyRx + (Hash01(44u, i) * CanopyRx * 2f) + (MathF.Sin((time * 1.6f) + i) * 14f);
            float ly = c.Y + (CanopyRy * 0.6f) + (phase * 300f);
            float spin = MathF.Sin((time * 3f) + i);
            batch.FillRectangle(new RectangleF(lx, ly, 3f + (MathF.Abs(spin) * 2f), 2.5f), new ColorRgba(150, 210, 100, (byte)(230f * (1f - phase))));
        }
    }

    // ── Pieces ──────────────────────────────────────────────────────────────

    private static void DrawRoots(GeometryBatch batch, Vector2 foot, float time)
    {
        float pulse = 0.5f + (0.5f * MathF.Sin(time * 1.1f));
        const int Roots = 7;
        const int Steps = 6;
        Span<Vector2> pts = stackalloc Vector2[Steps + 1];
        Span<float> widths = stackalloc float[Steps + 1];
        for (int i = 0; i < Roots; i++)
        {
            // Spread from left to right across the front of the trunk; outer roots run sideways, inner
            // roots towards the viewer. Each one starts thick at the trunk and tapers as it sinks.
            float u = i / (float)(Roots - 1);
            float a = MathF.PI * (0.08f + (u * 0.84f));
            Vector2 dir = new(-MathF.Cos(a), MathF.Sin(a) * 0.5f);
            Vector2 start = foot + new Vector2(dir.X * 62f, -22f + (dir.Y * 20f));
            float len = 60f + (Hash01(51u, i) * 45f) + (MathF.Abs(dir.X) * 30f);
            float bend = (Hash01(52u, i) - 0.5f) * 0.6f;
            for (int s = 0; s <= Steps; s++)
            {
                float t = s / (float)Steps;
                pts[s] = start + (Rotate(dir, bend * t) * len * t) + new Vector2(0f, (t * 14f) + (MathF.Sin(t * MathF.PI) * -6f));
                widths[s] = MathF.Max(30f * MathF.Pow(1f - t, 1.2f), 3f);
            }

            for (int s = 0; s < Steps; s++)
            {
                Vector2 d = Vector2.Normalize(pts[s + 1] - pts[s]);
                Vector2 n = new(-d.Y, d.X);
                Vector2 a0 = pts[s] + (n * widths[s] * 0.5f);
                Vector2 a1 = pts[s] - (n * widths[s] * 0.5f);
                Vector2 b0 = pts[s + 1] + (n * widths[s + 1] * 0.5f);
                Vector2 b1 = pts[s + 1] - (n * widths[s + 1] * 0.5f);
                batch.FillTriangle(a0, a1, b1, s_bark);
                batch.FillTriangle(a0, b1, b0, s_bark);

                // Lit top edge and shaded underside give the root volume
                Vector2 up = n.Y < 0f ? n : -n;
                batch.FillTriangle(pts[s] + (up * widths[s] * 0.5f), pts[s] + (up * widths[s] * 0.15f), pts[s + 1] + (up * widths[s + 1] * 0.15f), s_barkLight);
                batch.FillTriangle(pts[s] + (up * widths[s] * 0.5f), pts[s + 1] + (up * widths[s + 1] * 0.15f), pts[s + 1] + (up * widths[s + 1] * 0.5f), s_barkLight);
                batch.FillTriangle(pts[s] - (up * widths[s] * 0.5f), pts[s] - (up * widths[s] * 0.2f), pts[s + 1] - (up * widths[s + 1] * 0.2f), s_barkDeep);
                batch.FillTriangle(pts[s] - (up * widths[s] * 0.5f), pts[s + 1] - (up * widths[s + 1] * 0.2f), pts[s + 1] - (up * widths[s + 1] * 0.5f), s_barkDeep);
            }

            // Spirit light running along every other root
            if (i % 2 == 1)
            {
                for (int s = 0; s < Steps - 1; s++)
                {
                    batch.DrawLine(pts[s], pts[s + 1], new ColorRgba(s_spirit.R, s_spirit.G, s_spirit.B, (byte)(40 + (pulse * 70f))), 2f);
                }
            }
        }
    }

    private static void DrawLimbs(GeometryBatch batch, Vector2 topLeft, Vector2 topRight)
    {
        Vector2 crotch = (topLeft + topRight) * 0.5f;
        float[] angles = [-2.35f, -1.95f, -1.55f, -1.2f, -0.8f];
        for (int i = 0; i < angles.Length; i++)
        {
            Vector2 start = crotch + new Vector2((i - 2) * 12f, 6f);
            Vector2 dir = new(MathF.Cos(angles[i]), MathF.Sin(angles[i]));
            float len = 95f + (Hash01(61u, i) * 50f);
            Vector2 mid = start + (dir * len * 0.55f);
            Vector2 end = start + (Rotate(dir, (i - 2) * 0.18f) * len);
            TaperedSegment(batch, start, mid, 26f, 17f, s_bark);
            TaperedSegment(batch, mid, end, 17f, 7f, s_bark);
            batch.DrawLine(start + new Vector2(-6f, 0f), mid + new Vector2(-4f, 0f), s_barkLight, 2f);
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static (Vector2 Offset, float Radius, float Tone)[] BuildClumps()
    {
        // Sample a lumpy dome, then sort back-to-front (top of the crown first) so lower clumps overlap.
        (Vector2 Offset, float Radius, float Tone)[] clumps = new (Vector2, float, float)[124];
        for (int i = 0; i < clumps.Length; i++)
        {
            float a = Hash01(71u, i) * MathF.Tau;
            float lump = 1f + (0.14f * MathF.Sin((a * 5f) + 0.7f)) + (0.07f * MathF.Sin(a * 11f));
            float d = MathF.Sqrt(Hash01(72u, i)) * lump * 0.88f;
            Vector2 off = new(MathF.Cos(a) * d * CanopyRx, MathF.Sin(a) * d * CanopyRy);
            float tone = Math.Clamp(0.5f - (off.Y / (CanopyRy * 2f)) + ((1f - d) * 0.25f), 0f, 1f);
            float r = 26f + (Hash01(73u, i) * 22f) + ((1f - d) * 10f);
            clumps[i] = (off, r, tone);
        }

        Array.Sort(clumps, (x, y) => x.Offset.Y.CompareTo(y.Offset.Y));
        return clumps;
    }

    private static void FillBand(GeometryBatch batch, ReadOnlySpan<Vector2> left, ReadOnlySpan<Vector2> right, float f0, float f1, ColorRgba color)
    {
        for (int i = 0; i + 1 < left.Length; i++)
        {
            Vector2 a0 = Lerp(left[i], right[i], f0);
            Vector2 a1 = Lerp(left[i], right[i], f1);
            Vector2 b0 = Lerp(left[i + 1], right[i + 1], f0);
            Vector2 b1 = Lerp(left[i + 1], right[i + 1], f1);
            batch.FillTriangle(a0, a1, b1, color);
            batch.FillTriangle(a0, b1, b0, color);
        }
    }

    private static void TaperedSegment(GeometryBatch batch, Vector2 a, Vector2 b, float wa, float wb, ColorRgba color)
    {
        Vector2 n = Vector2.Normalize(new Vector2(-(b.Y - a.Y), b.X - a.X));
        batch.FillTriangle(a - (n * wa * 0.5f), a + (n * wa * 0.5f), b + (n * wb * 0.5f), color);
        batch.FillTriangle(a - (n * wa * 0.5f), b + (n * wb * 0.5f), b - (n * wb * 0.5f), color);
        batch.FillCircle(b, wb * 0.5f, color, 8);
    }

    private static void FillEllipse(GeometryBatch batch, Vector2 c, float rx, float ry, ColorRgba color, int segments)
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

    private static Vector2 Rotate(Vector2 v, float a) =>
        new((v.X * MathF.Cos(a)) - (v.Y * MathF.Sin(a)), (v.X * MathF.Sin(a)) + (v.Y * MathF.Cos(a)));

    private static Vector2 Lerp(Vector2 a, Vector2 b, float t) => a + ((b - a) * t);

    private static float Hash01(uint seed, int i) => AmbientAnimator.Hash01(seed, i);

    private static ColorRgba Mix(ColorRgba a, ColorRgba b, float t) => new(
        (byte)(a.R + ((b.R - a.R) * t)),
        (byte)(a.G + ((b.G - a.G) * t)),
        (byte)(a.B + ((b.B - a.B) * t)),
        (byte)(a.A + ((b.A - a.A) * t)));

    private static ColorRgba Shade(ColorRgba c, float k) =>
        new((byte)Math.Clamp(c.R * k, 0f, 255f), (byte)Math.Clamp(c.G * k, 0f, 255f), (byte)Math.Clamp(c.B * k, 0f, 255f), c.A);
}
