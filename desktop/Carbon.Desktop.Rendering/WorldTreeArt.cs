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
    private const float TrunkHeight = 270f;
    private const float CanopyRise = 360f; // canopy centre above the foot
    private const float CanopyRx = 250f;
    private const float CanopyRy = 165f;

    private static readonly ColorRgba s_barkLight = new(146, 108, 74);
    private static readonly ColorRgba s_bark = new(104, 74, 50);
    private static readonly ColorRgba s_barkDark = new(64, 44, 30);
    private static readonly ColorRgba s_barkDeep = new(40, 27, 19);
    private static readonly ColorRgba s_moss = new(92, 140, 70);
    private static readonly ColorRgba s_spirit = new(140, 255, 214);

    // Foliage lobes (offset from the crown centre, radius), sorted back to front.
    private static readonly (Vector2 Offset, float Radius)[] s_lobes =
    [
        (new Vector2(-40f, -130f), 105f),
        (new Vector2(90f, -120f), 95f),
        (new Vector2(-170f, -60f), 100f),
        (new Vector2(175f, -50f), 100f),
        (new Vector2(0f, -40f), 125f),
        (new Vector2(-250f, 20f), 80f),
        (new Vector2(255f, 30f), 80f),
        (new Vector2(-120f, 55f), 100f),
        (new Vector2(125f, 65f), 100f),
        (new Vector2(5f, 85f), 95f),
    ];

    // Leaf clumps of a unit lobe: offset (in lobe radii), radius (in lobe radii), tone (0 = underside, 1 = sunlit top-left).
    private const int ClumpsPerLobe = 22;
    private static readonly (Vector2 Offset, float Radius, float Tone)[] s_lobeClumps = BuildLobeClumps();

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

        // A fairy ring of glowing mushrooms around the roots
        for (int i = 0; i < 14; i++)
        {
            float a = (i * (MathF.Tau / 14f)) + (Hash01(17u, i) * 0.2f);
            Vector2 p = foot + new Vector2(MathF.Cos(a) * 170f, (MathF.Sin(a) * 74f) + 6f);
            DrawFairyMushroom(batch, p, 7f + (Hash01(18u, i) * 4f), 1000u + (uint)i, time);
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

    /// <summary>
    /// The trunk (Y-sorted with entities): three strands braided around each other, each with its own
    /// cylindrical shading, drawn back-to-front per height step so the twists read clearly; great limbs
    /// at the top and roots at the base.
    /// </summary>
    public static void DrawTrunk(GeometryBatch batch, Vector2 foot, float time)
    {
        const int N = 22;
        const int Strands = 3;

        // Soft cyan aura and contact shadow at the base
        for (int i = 0; i < 4; i++)
        {
            FillEllipse(batch, foot + new Vector2(0f, -6f), 150f - (i * 26f), 46f - (i * 8f), new ColorRgba(s_spirit.R, s_spirit.G, s_spirit.B, 10), 28);
        }

        FillEllipse(batch, foot + new Vector2(16f, 4f), 120f, 22f, new ColorRgba(0, 0, 0, 70), 28);

        // Dark core so no grass shows between the strands
        Span<Vector2> coreL = stackalloc Vector2[N + 1];
        Span<Vector2> coreR = stackalloc Vector2[N + 1];
        for (int i = 0; i <= N; i++)
        {
            float t = i / (float)N;
            float half = 40f + (20f * (1f - t)) + (34f * MathF.Pow(1f - t, 4f));
            coreL[i] = new Vector2(foot.X - (half * 0.85f), foot.Y - (t * TrunkHeight));
            coreR[i] = new Vector2(foot.X + (half * 0.85f), foot.Y - (t * TrunkHeight));
        }

        FillBand(batch, coreL, coreR, 0f, 1f, s_barkDeep);

        // Strand geometry
        Span<Vector2> centre = stackalloc Vector2[(N + 1) * Strands];
        Span<float> depth = stackalloc float[(N + 1) * Strands];
        Span<float> width = stackalloc float[(N + 1) * Strands];
        for (int k = 0; k < Strands; k++)
        {
            for (int i = 0; i <= N; i++)
            {
                float t = i / (float)N;
                float phase = (t * 2.3f * MathF.PI) + (k * MathF.Tau / Strands);
                float spread = (24f * (1f - (t * 0.35f))) + (40f * MathF.Pow(1f - t, 3f));
                centre[(k * (N + 1)) + i] = new Vector2(foot.X + (MathF.Sin(phase) * spread), foot.Y - (t * TrunkHeight));
                depth[(k * (N + 1)) + i] = MathF.Cos(phase);
                width[(k * (N + 1)) + i] = (34f * (1f - (t * 0.42f))) + (26f * MathF.Pow(1f - t, 3f));
            }
        }

        Span<int> order = stackalloc int[Strands];
        for (int i = 0; i < N; i++)
        {
            // Back strands first at this height step
            for (int k = 0; k < Strands; k++)
            {
                order[k] = k;
            }

            for (int x = 1; x < Strands; x++)
            {
                for (int y = x; y > 0 && depth[(order[y] * (N + 1)) + i] < depth[(order[y - 1] * (N + 1)) + i]; y--)
                {
                    (order[y], order[y - 1]) = (order[y - 1], order[y]);
                }
            }

            for (int o = 0; o < Strands; o++)
            {
                int k = order[o];
                int a0 = (k * (N + 1)) + i;
                DrawStrandSegment(batch, centre[a0], centre[a0 + 1], width[a0], width[a0 + 1], depth[a0]);
            }
        }

        // Hollow with spirit light where the strands part, and carved runes
        float pulse = 0.5f + (0.5f * MathF.Sin(time * 1.4f));
        Vector2 hollow = foot + new Vector2(-4f, -TrunkHeight * 0.22f);
        FillEllipse(batch, hollow, 30f + (pulse * 6f), 36f + (pulse * 6f), new ColorRgba(s_spirit.R, s_spirit.G, s_spirit.B, (byte)(22 + (pulse * 22f))), 20);
        FillEllipse(batch, hollow, 12f, 18f, s_barkDeep, 16);
        FillEllipse(batch, hollow + new Vector2(0f, 3f), 7f, 12f, new ColorRgba(s_spirit.R, s_spirit.G, s_spirit.B, (byte)(110 + (pulse * 100f))), 14);

        int top = N;
        Vector2 crownL = centre[top];
        Vector2 crownR = centre[top];
        for (int k = 1; k < Strands; k++)
        {
            Vector2 c = centre[(k * (N + 1)) + top];
            crownL = c.X < crownL.X ? c : crownL;
            crownR = c.X > crownR.X ? c : crownR;
        }

        DrawLimbs(batch, crownL, crownR);
        DrawRoots(batch, foot, time);
    }

    private static void DrawStrandSegment(GeometryBatch batch, Vector2 a, Vector2 b, float wa, float wb, float depth)
    {
        // Strands facing the viewer are lighter; each is shaded like a cylinder lit from the left.
        float lightK = 0.55f + (0.45f * ((depth + 1f) * 0.5f));
        ColorRgba body = Mix(s_barkDark, s_bark, lightK);
        ColorRgba lit = Mix(s_bark, s_barkLight, lightK);
        ColorRgba shade = Mix(s_barkDeep, s_barkDark, lightK);
        Band(batch, a, b, wa, wb, -0.5f, 0.5f, body);
        Band(batch, a, b, wa, wb, -0.36f, -0.1f, lit);
        Band(batch, a, b, wa, wb, 0.28f, 0.5f, shade);
        Band(batch, a, b, wa, wb, -0.5f, -0.44f, s_barkDeep);
        Band(batch, a, b, wa, wb, 0.44f, 0.5f, s_barkDeep);
    }

    // A slice of a strand between fractions f0..f1 of its width (-0.5 = left edge, 0.5 = right edge).
    private static void Band(GeometryBatch batch, Vector2 a, Vector2 b, float wa, float wb, float f0, float f1, ColorRgba color)
    {
        Vector2 a0 = a + new Vector2(wa * f0, 0f);
        Vector2 a1 = a + new Vector2(wa * f1, 0f);
        Vector2 b0 = b + new Vector2(wb * f0, 0f);
        Vector2 b1 = b + new Vector2(wb * f1, 0f);
        batch.FillTriangle(a0, a1, b1, color);
        batch.FillTriangle(a0, b1, b0, color);
    }

    /// <summary>
    /// The crown, drawn above everything: great lobes of foliage growing from the limb ends, each built
    /// from leaf clumps with a shaded underside, sunlit rim and leaf texture; luminous vines hanging
    /// below and motes of light rising. While the player stands beneath it a window opens in the leaves.
    /// </summary>
    public static void DrawCanopy(GeometryBatch batch, Vector2 foot, float time, Vector2 player)
    {
        Vector2 c = foot + new Vector2(0f, -CanopyRise);
        float sway = MathF.Sin(time * 0.5f) * 2f;
        bool under = MathF.Abs(player.X - c.X) < CanopyRx + 40f && player.Y > c.Y - CanopyRy - 60f && player.Y < foot.Y - 120f;
        const float Window = 110f;

        ColorRgba deep = new(14, 58, 44);
        ColorRgba body = new(28, 110, 70);
        ColorRgba lit = new(84, 172, 96);
        ColorRgba sun = new(196, 232, 140);
        ColorRgba leafDark = new(10, 46, 34);

        // Hanging vines (behind the lowest leaves)
        for (int i = 0; i < 16; i++)
        {
            float x = c.X - (CanopyRx * 0.85f) + (i * (CanopyRx * 1.7f / 15f)) + ((Hash01(81u, i) - 0.5f) * 20f);
            float y0 = c.Y + (CanopyRy * 0.45f) + (Hash01(82u, i) * 30f);
            float len = 50f + (Hash01(83u, i) * 90f);
            if (under && MathF.Abs(x - player.X) < Window * 0.8f && player.Y > y0 && player.Y < y0 + len + 30f)
            {
                continue;
            }

            Vector2 prev = new(x, y0);
            for (int sgm = 1; sgm <= 4; sgm++)
            {
                float t = sgm / 4f;
                Vector2 p = new(x + (MathF.Sin((time * 1.1f) + i + (t * 2f)) * 6f * t), y0 + (len * t));
                batch.DrawLine(prev, p, new ColorRgba(36, 90, 56), 2.2f - t);
                prev = p;
            }

            float glow = 0.5f + (0.5f * MathF.Sin((time * 2f) + (i * 1.3f)));
            ColorRgba bead = i % 3 == 0 ? new ColorRgba(255, 226, 150) : s_spirit;
            batch.FillCircle(prev, 8f + (glow * 3f), new ColorRgba(bead.R, bead.G, bead.B, (byte)(30 + (glow * 30f))), 12);
            batch.FillCircle(prev, 2.8f, bead, 8);
        }

        // Lobes, back to front
        for (int l = 0; l < s_lobes.Length; l++)
        {
            (Vector2 lobeOff, float lobeR) = s_lobes[l];
            Vector2 lc = c + lobeOff + new Vector2(sway * (0.5f + (l * 0.08f)), 0f);
            float global = Math.Clamp(0.5f - (lobeOff.Y / 260f), 0f, 1f); // higher lobes are brighter

            // Underside shadow of the lobe
            if (!under || Vector2.Distance(lc, player) > lobeR + Window)
            {
                FillEllipse(batch, lc + new Vector2(6f, lobeR * 0.35f), lobeR * 0.95f, lobeR * 0.55f, new ColorRgba(deep.R, deep.G, deep.B, 230), 24);
            }

            for (int i = 0; i < ClumpsPerLobe; i++)
            {
                (Vector2 off, float r, float tone) = s_lobeClumps[i];
                Vector2 p = lc + (off * lobeR);
                float cr = r * lobeR;
                if (under)
                {
                    float d = Vector2.Distance(p, player);
                    if (d < Window)
                    {
                        continue;
                    }

                    cr *= Math.Clamp((d - Window) / 40f, 0.45f, 1f);
                }

                float k = Math.Clamp((tone * 0.7f) + (global * 0.3f), 0f, 1f);
                ColorRgba main = k < 0.5f ? Mix(deep, body, k * 2f) : Mix(body, lit, (k - 0.5f) * 2f);
                batch.FillCircle(p, cr, main, 12);
                if (tone > 0.7f)
                {
                    // Sunlit rim on the upper-left of the lobe
                    batch.FillCircle(p + new Vector2(-cr * 0.25f, -cr * 0.3f), cr * 0.55f, Mix(lit, sun, (tone - 0.7f) / 0.3f), 10);
                }
            }

            // Leaf texture: short dark and light strokes scattered over the lobe
            for (int i = 0; i < 18; i++)
            {
                float a = Hash01((uint)(90 + l), i) * MathF.Tau;
                float d = MathF.Sqrt(Hash01((uint)(110 + l), i)) * lobeR * 0.8f;
                Vector2 p = lc + new Vector2(MathF.Cos(a) * d, MathF.Sin(a) * d * 0.8f);
                if (under && Vector2.Distance(p, player) < Window + 10f)
                {
                    continue;
                }

                bool upper = MathF.Sin(a) < 0f;
                ColorRgba stroke = upper ? new ColorRgba(sun.R, sun.G, sun.B, 150) : new ColorRgba(leafDark.R, leafDark.G, leafDark.B, 150);
                batch.DrawLine(p, p + new Vector2(4f, upper ? -3f : 3f), stroke, 2f);
            }
        }

        // Luminous blossoms breathing slowly across the crown
        for (int i = 0; i < 24; i++)
        {
            (Vector2 lobeOff, float lobeR) = s_lobes[i % s_lobes.Length];
            float a = Hash01(31u, i) * MathF.Tau;
            float d = MathF.Sqrt(Hash01(32u, i)) * lobeR * 0.75f;
            Vector2 p = c + lobeOff + new Vector2(MathF.Cos(a) * d, MathF.Sin(a) * d * 0.7f);
            if (under && Vector2.Distance(p, player) < Window)
            {
                continue;
            }

            float b = 0.5f + (0.5f * MathF.Sin((time * 1.3f) + (i * 0.9f)));
            ColorRgba petal = i % 3 == 0 ? new ColorRgba(255, 236, 170) : new ColorRgba(200, 255, 236);
            batch.FillCircle(p, 6f + (b * 3f), new ColorRgba(petal.R, petal.G, petal.B, (byte)(25 + (b * 30f))), 10);
            batch.FillCircle(p, 2.2f, petal, 6);
        }

        // Soft light shafts
        for (int i = 0; i < 4; i++)
        {
            float pulse = 0.5f + (0.5f * MathF.Sin((time * 0.6f) + (i * 1.9f)));
            float x = c.X - 170f + (i * 100f);
            for (int k = 0; k < 3; k++)
            {
                float w = 10f + (k * 9f);
                Vector2 a0 = new(x - w, c.Y + 70f);
                Vector2 a1 = new(x + w, c.Y + 70f);
                Vector2 b0 = new(x - w + 60f, foot.Y + 20f);
                Vector2 b1 = new(x + w + 70f, foot.Y + 20f);
                ColorRgba light = new(255, 246, 205, (byte)((9 + (pulse * 9f)) / (k + 1)));
                batch.FillTriangle(a0, a1, b1, light);
                batch.FillTriangle(a0, b1, b0, light);
            }
        }

        // Motes of spirit light rising from the roots into the crown
        for (int i = 0; i < 20; i++)
        {
            float phase = AmbientAnimator.Frac((time * (0.06f + (Hash01(45u, i) * 0.05f))) + Hash01(46u, i));
            float x = foot.X + ((Hash01(47u, i) - 0.5f) * 220f) + (MathF.Sin((time * 0.9f) + i) * 10f);
            float y = foot.Y - (phase * (CanopyRise + 40f));
            float alpha = MathF.Sin(phase * MathF.PI);
            batch.FillCircle(new Vector2(x, y), 5f, new ColorRgba(s_spirit.R, s_spirit.G, s_spirit.B, (byte)(30 * alpha)), 8);
            batch.FillCircle(new Vector2(x, y), 1.6f, new ColorRgba(220, 255, 240, (byte)(220 * alpha)), 6);
        }

        // Fireflies and falling leaves
        for (int i = 0; i < 16; i++)
        {
            float t = time * (0.2f + (Hash01(41u, i) * 0.25f));
            Vector2 p = new(foot.X + (MathF.Sin(t + (i * 1.7f)) * CanopyRx), foot.Y - 50f + (MathF.Cos((t * 1.3f) + i) * 80f));
            float glow = 0.5f + (0.5f * MathF.Sin((time * 4f) + (i * 2.3f)));
            batch.FillCircle(p, 7f, new ColorRgba(253, 230, 138, (byte)(35 * glow)), 10);
            batch.FillCircle(p, 1.8f, new ColorRgba(255, 250, 200, (byte)(110 + (130 * glow))), 6);
        }

        for (int i = 0; i < 12; i++)
        {
            float phase = AmbientAnimator.Frac((time * 0.11f) + Hash01(43u, i));
            float lx = c.X - CanopyRx + (Hash01(44u, i) * CanopyRx * 2f) + (MathF.Sin((time * 1.6f) + i) * 14f);
            float ly = c.Y + (CanopyRy * 0.7f) + (phase * 280f);
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

    private static (Vector2 Offset, float Radius, float Tone)[] BuildLobeClumps()
    {
        (Vector2 Offset, float Radius, float Tone)[] clumps = new (Vector2, float, float)[ClumpsPerLobe];
        for (int i = 0; i < clumps.Length; i++)
        {
            float a = Hash01(71u, i) * MathF.Tau;
            float d = MathF.Sqrt(Hash01(72u, i)) * 0.62f;
            Vector2 off = new(MathF.Cos(a) * d, MathF.Sin(a) * d * 0.78f);
            float tone = Math.Clamp(0.55f - (off.Y * 0.9f) - (off.X * 0.35f), 0f, 1f);
            clumps[i] = (off, 0.3f + (Hash01(73u, i) * 0.16f), tone);
        }

        // Back to front within the lobe as well
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

    private static void DrawFairyMushroom(GeometryBatch batch, Vector2 pos, float size, uint seed, float time)
    {
        float pulse = 0.72f + (0.28f * MathF.Sin((time * 2.2f) + (Hash01(seed, 1) * MathF.Tau)));
        FillEllipse(batch, pos + new Vector2(0.5f, 1.5f), size * 0.85f, size * 0.42f, new ColorRgba(0, 0, 0, 42), 10);
        FillEllipse(batch, pos + new Vector2(0f, -size * 0.25f), size * 0.26f, size * 0.42f, new ColorRgba(210, 240, 240), 8);
        FillEllipse(batch, pos + new Vector2(0f, -size * 0.62f), size * 0.72f, size * 0.46f, new ColorRgba(34, 211, 238, (byte)(210 * pulse)), 12);
        FillEllipse(batch, pos + new Vector2(-size * 0.22f, -size * 0.74f), size * 0.24f, size * 0.14f, new ColorRgba(224, 250, 255, 225), 8);
    }

    private static ColorRgba Shade(ColorRgba c, float k) =>
        new((byte)Math.Clamp(c.R * k, 0f, 255f), (byte)Math.Clamp(c.G * k, 0f, 255f), (byte)Math.Clamp(c.B * k, 0f, 255f), c.A);
}
