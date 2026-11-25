// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Maps.Props;

namespace Carbon.Desktop.Rendering;

/// <summary>
/// Procedural art for natural props (trees, bushes, cacti, rocks, ores, crystals) in which every
/// instance looks a little different. A stable hash of the prop's position picks its palette shift,
/// leaf-cluster layout, lean, branch arrangement and crystal pattern, so the same tree looks the
/// same every time it is drawn, while no two neighbours are clones.
/// </summary>
internal static class PropArt
{
    private static readonly ColorRgba s_shadow = new(0, 0, 0, 45);

    /// <summary>Draws the ground layer of a prop (trunk, stem, rock body), Y-sorted with entities.</summary>
    public static void DrawGround(GeometryBatch batch, in MapProp prop)
    {
        Vector2 pos = prop.WorldPosition;
        uint h = Seed(prop);
        ColorRgba primary = Hex(prop.ColorPrimary);
        ColorRgba secondary = Vary(Hex(prop.ColorSecondary), h, 1, 0.12f);
        float r = prop.Radius;
        float lean = Lean(h);

        switch (prop.Type)
        {
            case MapPropType.OakTree:
            case MapPropType.WillowTree:
            case MapPropType.CherryBlossomTree:
                {
                    float tw = prop.Type == MapPropType.WillowTree ? 16f : 13f + (Hash01(h, 2) * 4f);
                    batch.FillCircle(new Vector2(pos.X + 4f, pos.Y + 14f), r * 0.45f, s_shadow);
                    DrawTrunk(batch, pos, tw, 26f, lean, secondary, h);
                    if (prop.Type == MapPropType.CherryBlossomTree)
                    {
                        batch.FillCircle(new Vector2(pos.X, pos.Y + 10f), r * 0.65f, new ColorRgba(244, 114, 182, 60));
                    }

                    break;
                }

            case MapPropType.BirchTree:
                batch.FillCircle(new Vector2(pos.X, pos.Y + 10f), r * 0.4f, s_shadow);
                DrawTrunk(batch, pos, 9f + (Hash01(h, 2) * 2f), 30f, lean, new ColorRgba(236, 232, 222), h);
                for (int i = 0; i < 3 + (int)(Hash01(h, 3) * 3f); i++)
                {
                    float y = pos.Y - 6f + (Hash01(h, 10 + i) * 24f);
                    float x = pos.X - 4f + (Hash01(h, 20 + i) * 5f) + (lean * (pos.Y + 16f - y) / 30f);
                    batch.FillRectangle(new RectangleF(x, y, 3f + (Hash01(h, 30 + i) * 3f), 2f), new ColorRgba(30, 41, 59));
                }

                break;

            case MapPropType.PineTree:
            case MapPropType.SnowPine:
                batch.FillCircle(new Vector2(pos.X, pos.Y + 8f), r * 0.35f, s_shadow);
                DrawTrunk(batch, pos, 9f + (Hash01(h, 2) * 3f), 24f, lean * 0.4f, new ColorRgba(113, 63, 18), h);
                break;

            case MapPropType.PalmTree:
                {
                    // A curved trunk: segments stepping sideways
                    batch.FillCircle(new Vector2(pos.X, pos.Y + 8f), r * 0.35f, new ColorRgba(0, 0, 0, 40));
                    float bend = (Hash01(h, 2) - 0.5f) * 18f;
                    for (int i = 0; i < 6; i++)
                    {
                        float t = i / 6f;
                        float x = pos.X + (bend * t * t);
                        float y = pos.Y + 22f - (t * 36f);
                        batch.FillRectangle(new RectangleF(x - 4f, y - 6f, 8f, 7f), (i & 1) == 0 ? secondary : Shade(secondary, 0.85f));
                    }

                    break;
                }

            case MapPropType.AcaciaTree:
                {
                    batch.FillCircle(new Vector2(pos.X, pos.Y + 8f), r * 0.4f, s_shadow);
                    DrawTrunk(batch, pos, 9f, 28f, lean, secondary, h);
                    float fork = 10f + (Hash01(h, 3) * 6f);
                    batch.DrawLine(new Vector2(pos.X + lean, pos.Y - 12f), new Vector2(pos.X - fork, pos.Y - 22f), secondary, 5f);
                    batch.DrawLine(new Vector2(pos.X + lean, pos.Y - 12f), new Vector2(pos.X + fork + 2f, pos.Y - 25f), secondary, 5f);
                    break;
                }

            case MapPropType.DeadTree:
                {
                    batch.FillCircle(new Vector2(pos.X, pos.Y + 8f), r * 0.3f, s_shadow);
                    DrawTrunk(batch, pos, 10f, 32f, lean, primary, h);
                    int branches = 2 + (int)(Hash01(h, 3) * 3f);
                    for (int i = 0; i < branches; i++)
                    {
                        float y = pos.Y - 18f + (i * 7f);
                        float side = ((h >> i) & 1) == 0 ? -1f : 1f;
                        float len = 12f + (Hash01(h, 40 + i) * 10f);
                        Vector2 a = new(pos.X + lean, y);
                        Vector2 b = a + new Vector2(side * len, -6f - (Hash01(h, 50 + i) * 8f));
                        batch.DrawLine(a, b, secondary, 4f);
                        batch.DrawLine(b, b + new Vector2(side * 5f, -6f), secondary, 2.5f);
                    }

                    break;
                }

            case MapPropType.Cactus:
                {
                    batch.FillCircle(new Vector2(pos.X, pos.Y + 6f), r * 0.35f, new ColorRgba(0, 0, 0, 40));
                    float height = 34f + (Hash01(h, 2) * 14f);
                    batch.FillRectangle(new RectangleF(pos.X - 6f, pos.Y + 20f - height, 12f, height), primary);
                    batch.FillRectangle(new RectangleF(pos.X - 3f, pos.Y + 22f - height, 2f, height - 4f), Shade(primary, 1.2f));
                    int arms = (int)(Hash01(h, 3) * 3f);
                    for (int i = 0; i < arms; i++)
                    {
                        float side = i == 0 ? -1f : 1f;
                        float y = pos.Y - 4f - (Hash01(h, 20 + i) * 14f);
                        float up = 10f + (Hash01(h, 30 + i) * 10f);
                        float ax = side < 0f ? pos.X - 16f : pos.X + 6f;
                        batch.FillRectangle(new RectangleF(ax, y, 10f, 6f), primary);
                        batch.FillRectangle(new RectangleF(side < 0f ? pos.X - 16f : pos.X + 10f, y - up, 6f, up + 4f), secondary);
                    }

                    if (Hash01(h, 9) > 0.7f)
                    {
                        batch.FillCircle(new Vector2(pos.X, pos.Y + 20f - height), 3.5f, new ColorRgba(244, 114, 182), 8); // flower
                    }

                    break;
                }

            case MapPropType.Bush:
                {
                    int blobs = 3 + (int)(Hash01(h, 2) * 3f);
                    ColorRgba dark = Vary(primary, h, 3, 0.15f);
                    ColorRgba light = Vary(Hex(prop.ColorSecondary), h, 4, 0.15f);
                    for (int i = 0; i < blobs; i++)
                    {
                        float a = (i / (float)blobs * MathF.Tau) + Hash01(h, 10 + i);
                        Vector2 c = pos + new Vector2(MathF.Cos(a) * r * 0.5f, (MathF.Sin(a) * r * 0.3f) + 2f);
                        batch.FillCircle(c, r * (0.55f + (Hash01(h, 20 + i) * 0.3f)), dark, 12);
                    }

                    batch.FillCircle(new Vector2(pos.X, pos.Y - 4f), r * (0.8f + (Hash01(h, 5) * 0.25f)), light, 14);
                    if (Hash01(h, 6) > 0.6f)
                    {
                        // Berries
                        ColorRgba berry = Hash01(h, 7) > 0.5f ? new ColorRgba(220, 38, 38) : new ColorRgba(96, 80, 200);
                        for (int i = 0; i < 4; i++)
                        {
                            batch.FillCircle(pos + new Vector2((Hash01(h, 60 + i) - 0.5f) * r * 1.4f, (Hash01(h, 70 + i) - 0.6f) * r), 2f, berry, 6);
                        }
                    }

                    break;
                }

            case MapPropType.MushroomCluster:
                // Each patch is its own species: dome, parasol, cone, bell, fly agaric or glowing caps.
                MushroomArt.DrawCluster(batch, pos + new Vector2(0f, 6f), r * 1.3f, h);
                break;

            case MapPropType.CopperVein:
            case MapPropType.IronVein:
            case MapPropType.GoldVein:
            case MapPropType.CoalDeposit:
            case MapPropType.SilverVein:
                DrawRock(batch, pos, r, Vary(primary, h, 2, 0.1f), h);
                DrawOreNuggets(batch, pos, r, Hex(prop.ColorSecondary), h, prop.Type == MapPropType.CoalDeposit);
                break;

            case MapPropType.Boulder:
                DrawRock(batch, pos, r, Vary(primary, h, 2, 0.12f), h);
                if (Hash01(h, 8) > 0.55f)
                {
                    // Moss on the top
                    batch.FillCircle(pos + new Vector2(-r * 0.2f, -r * 0.45f), r * 0.35f, new ColorRgba(74, 124, 60, 200), 8);
                }

                break;

            case MapPropType.CrystalCluster:
            case MapPropType.GlacialCrystal:
                DrawCrystals(batch, pos, r, primary, secondary, h, prop.Type == MapPropType.GlacialCrystal);
                break;

            case MapPropType.BasaltPillar:
                {
                    batch.FillCircle(new Vector2(pos.X, pos.Y + 8f), r * 0.85f, new ColorRgba(0, 0, 0, 60), segments: 6);
                    int columns = 2 + (int)(Hash01(h, 2) * 3f);
                    for (int i = 0; i < columns; i++)
                    {
                        Vector2 p = pos + new Vector2((i - ((columns - 1) * 0.5f)) * r * 0.55f, (Hash01(h, 10 + i) - 0.5f) * 6f);
                        float height = 10f + (Hash01(h, 20 + i) * 16f);
                        batch.FillRectangle(new RectangleF(p.X - (r * 0.28f), p.Y - height, r * 0.56f, height), Shade(primary, 0.8f));
                        batch.FillCircle(new Vector2(p.X, p.Y - height), r * 0.3f, secondary, segments: 6);
                    }

                    break;
                }

            case MapPropType.AncientShrine:
                batch.FillCircle(pos, r + 12f, new ColorRgba(129, 140, 248, 65));
                batch.FillRectangle(new RectangleF(pos.X - 22f, pos.Y + 4f, 44f, 16f), secondary);
                batch.FillRectangle(new RectangleF(pos.X - 12f, pos.Y - 28f, 24f, 40f), secondary);
                batch.FillRectangle(new RectangleF(pos.X - 6f, pos.Y - 20f, 12f, 24f), primary);
                break;

            case MapPropType.None:
            default:
                break;
        }
    }

    /// <summary>Returns the light a prop gives off at night (crystals, glowing mushrooms), if any.</summary>
    public static bool TryGetLight(in MapProp prop, out PointLight light)
    {
        if (prop.Type == MapPropType.CrystalCluster)
        {
            light = new PointLight(prop.WorldPosition + new Vector2(0f, -6f), 70f, Hex(prop.ColorPrimary), 0.7f);
            return true;
        }

        if (prop.Type == MapPropType.GlacialCrystal)
        {
            light = new PointLight(prop.WorldPosition + new Vector2(0f, -6f), 60f, new ColorRgba(170, 220, 255), 0.6f);
            return true;
        }

        if (prop.Type == MapPropType.MushroomCluster && MushroomArt.IsGlowing(Seed(prop)))
        {
            light = new PointLight(prop.WorldPosition, 60f, new ColorRgba(120, 230, 230), 0.8f);
            return true;
        }

        light = default;
        return false;
    }

    /// <summary>Draws the canopy of a tree above entities. <paramref name="alpha"/> fades it when the player is under it.</summary>
    /// <remarks>
    /// <paramref name="swayX"/> only shifts where the canopy is drawn. The look is always derived from the
    /// prop's fixed position, so wind sway never changes a tree's palette or leaf layout.
    /// </remarks>
    public static void DrawCanopy(GeometryBatch batch, in MapProp prop, byte alpha, float swayX = 0f)
    {
        uint h = Seed(prop);
        Vector2 pos = prop.WorldPosition + new Vector2(swayX, 0f);
        float r = prop.Radius;
        Vector2 top = pos + new Vector2(Lean(h), 0f);

        switch (prop.Type)
        {
            case MapPropType.OakTree:
                DrawLeafCloud(batch, top + new Vector2(0f, -26f), r, LeafPalette(h, s_oakPalettes), h, alpha, blobs: 5 + (int)(Hash01(h, 5) * 3f));
                break;

            case MapPropType.BirchTree:
                DrawLeafCloud(batch, top + new Vector2(0f, -28f), r * 0.9f, LeafPalette(h, s_birchPalettes), h, alpha, blobs: 4 + (int)(Hash01(h, 5) * 2f));
                break;

            case MapPropType.CherryBlossomTree:
                DrawLeafCloud(batch, top + new Vector2(0f, -28f), r, LeafPalette(h, s_cherryPalettes), h, alpha, blobs: 5 + (int)(Hash01(h, 5) * 3f));
                break;

            case MapPropType.WillowTree:
                {
                    (ColorRgba dark, ColorRgba mid, ColorRgba light) = LeafPalette(h, s_willowPalettes);
                    DrawLeafCloud(batch, top + new Vector2(0f, -22f), r, (dark, mid, light), h, alpha, blobs: 4);

                    // Hanging fronds of varying length
                    int fronds = 5 + (int)(Hash01(h, 6) * 4f);
                    for (int i = 0; i < fronds; i++)
                    {
                        float x = top.X - r + (i * (2f * r / (fronds - 1)));
                        float len = 12f + (Hash01(h, 40 + i) * 14f);
                        batch.FillRectangle(new RectangleF(x - 1.5f, top.Y - 16f, 3f, len), WithAlpha(i % 2 == 0 ? mid : dark, alpha));
                    }

                    break;
                }

            case MapPropType.AcaciaTree:
                {
                    (ColorRgba dark, ColorRgba mid, ColorRgba light) = LeafPalette(h, s_acaciaPalettes);
                    float spread = r * (1.1f + (Hash01(h, 6) * 0.4f));
                    Vector2 c = top + new Vector2(0f, -32f);
                    batch.FillRectangle(new RectangleF(c.X - spread, c.Y + 2f, spread * 2f, 9f), WithAlpha(dark, alpha));
                    int puffs = 4 + (int)(Hash01(h, 7) * 3f);
                    for (int i = 0; i < puffs; i++)
                    {
                        float x = c.X - spread + ((i + 0.5f) * (2f * spread / puffs));
                        batch.FillCircle(new Vector2(x, c.Y + (Hash01(h, 50 + i) * 4f)), r * (0.35f + (Hash01(h, 60 + i) * 0.2f)), WithAlpha(mid, alpha), 12);
                    }

                    batch.FillRectangle(new RectangleF(c.X - (spread * 0.8f), c.Y - 3f, spread * 1.6f, 5f), WithAlpha(light, alpha));
                    break;
                }

            case MapPropType.PineTree:
            case MapPropType.SnowPine:
                {
                    (ColorRgba dark, ColorRgba mid, ColorRgba _) = LeafPalette(h, s_pinePalettes);
                    int tiers = 3 + (int)(Hash01(h, 5) * 2f);
                    float height = 40f + (Hash01(h, 6) * 14f);
                    for (int i = 0; i < tiers; i++)
                    {
                        float t = i / (float)tiers;
                        float y = pos.Y - 4f - (t * height);
                        float w = r * (1.05f - (t * 0.7f)) * (0.9f + (Hash01(h, 20 + i) * 0.2f));
                        float x = pos.X + (Lean(h) * 0.4f * t);
                        batch.FillTriangle(new Vector2(x - w, y + 6f), new Vector2(x + w, y + 6f), new Vector2(x, y - 16f), WithAlpha(i % 2 == 0 ? dark : mid, alpha));
                        if (prop.Type == MapPropType.SnowPine)
                        {
                            batch.FillTriangle(new Vector2(x - (w * 0.45f), y - 6f), new Vector2(x + (w * 0.45f), y - 6f), new Vector2(x, y - 16f), WithAlpha(new ColorRgba(240, 246, 250), alpha));
                        }
                    }

                    break;
                }

            case MapPropType.PalmTree:
                {
                    (ColorRgba dark, ColorRgba mid, ColorRgba _) = LeafPalette(h, s_palmPalettes);
                    float bend = (Hash01(h, 2) - 0.5f) * 18f;
                    Vector2 crown = new(pos.X + bend, pos.Y - 18f);
                    int fronds = 5 + (int)(Hash01(h, 5) * 3f);
                    float rot = Hash01(h, 6) * MathF.Tau;
                    for (int i = 0; i < fronds; i++)
                    {
                        float a = rot + (i * (MathF.Tau / fronds));
                        Vector2 tip = crown + new Vector2(MathF.Cos(a) * r * 1.1f, (MathF.Sin(a) * r * 0.65f) + 8f);
                        Vector2 side = Vector2.Normalize(new Vector2(-(tip.Y - crown.Y), tip.X - crown.X)) * 6f;
                        batch.FillTriangle(crown + side, crown - side, tip, WithAlpha(i % 2 == 0 ? dark : mid, alpha));
                    }

                    batch.FillCircle(crown, 5f, WithAlpha(new ColorRgba(120, 84, 40), alpha), 8);
                    if (Hash01(h, 7) > 0.5f)
                    {
                        batch.FillCircle(crown + new Vector2(-4f, 4f), 3f, WithAlpha(new ColorRgba(110, 74, 30), alpha), 6); // coconuts
                        batch.FillCircle(crown + new Vector2(3f, 5f), 3f, WithAlpha(new ColorRgba(110, 74, 30), alpha), 6);
                    }

                    break;
                }

            case MapPropType.None:
            case MapPropType.DeadTree:
            case MapPropType.Cactus:
            case MapPropType.Bush:
            case MapPropType.MushroomCluster:
            case MapPropType.CopperVein:
            case MapPropType.IronVein:
            case MapPropType.GoldVein:
            case MapPropType.CoalDeposit:
            case MapPropType.SilverVein:
            case MapPropType.CrystalCluster:
            case MapPropType.Boulder:
            case MapPropType.BasaltPillar:
            case MapPropType.AncientShrine:
            case MapPropType.GlacialCrystal:
            default:
                break;
        }
    }

    // ── Trees ───────────────────────────────────────────────────────────────

    private static readonly (uint Dark, uint Mid, uint Light)[] s_oakPalettes =
    [
        (0x14532DFF, 0x16A34AFF, 0x4ADE80FF),
        (0x1F4D24FF, 0x2E7D32FF, 0x7CB342FF),
        (0x3F4A1EFF, 0x65762CFF, 0xA3B442FF),
        (0x7C2D12FF, 0xC2410CFF, 0xFB923CFF), // autumn
    ];

    private static readonly (uint Dark, uint Mid, uint Light)[] s_birchPalettes =
    [
        (0x3F6212FF, 0x84CC16FF, 0xBEF264FF),
        (0x4D7C0FFF, 0xA3E635FF, 0xD9F99DFF),
        (0x854D0EFF, 0xEAB308FF, 0xFDE047FF), // golden
    ];

    private static readonly (uint Dark, uint Mid, uint Light)[] s_cherryPalettes =
    [
        (0xBE185DFF, 0xF472B6FF, 0xFBCFE8FF),
        (0x9D174DFF, 0xEC4899FF, 0xF9A8D4FF),
        (0xC084FCFF, 0xE9D5FFFF, 0xFFFFFFFF), // white blossom
    ];

    private static readonly (uint Dark, uint Mid, uint Light)[] s_willowPalettes =
    [
        (0x365314FF, 0x65A30DFF, 0xA3E635FF),
        (0x14532DFF, 0x4D7C0FFF, 0x84CC16FF),
    ];

    private static readonly (uint Dark, uint Mid, uint Light)[] s_acaciaPalettes =
    [
        (0x4D5B1AFF, 0x7A8F2AFF, 0xA3A332FF),
        (0x3F5A1AFF, 0x5F7F24FF, 0x9AB83CFF),
    ];

    private static readonly (uint Dark, uint Mid, uint Light)[] s_pinePalettes =
    [
        (0x064E3BFF, 0x065F46FF, 0x10B981FF),
        (0x14532DFF, 0x166534FF, 0x22C55EFF),
        (0x1E3A3AFF, 0x2F5D50FF, 0x3F7F6BFF),
    ];

    private static readonly (uint Dark, uint Mid, uint Light)[] s_palmPalettes =
    [
        (0x15803DFF, 0x22C55EFF, 0x86EFACFF),
        (0x3F6212FF, 0x65A30DFF, 0xA3E635FF),
    ];

    private static (ColorRgba Dark, ColorRgba Mid, ColorRgba Light) LeafPalette(uint h, (uint Dark, uint Mid, uint Light)[] palettes)
    {
        // Rare palettes (autumn, golden, white) are the last entry and only picked ~7% of the time.
        int index = Hash01(h, 90) < 0.07f ? palettes.Length - 1 : (int)(Hash01(h, 91) * (palettes.Length - 1));
        (uint d, uint m, uint l) = palettes[Math.Clamp(index, 0, palettes.Length - 1)];
        return (Vary(Hex(d), h, 92, 0.08f), Vary(Hex(m), h, 93, 0.08f), Vary(Hex(l), h, 94, 0.08f));
    }

    // A canopy built from overlapping leaf clusters in hashed positions: dark base, mid body, lit top-left.
    private static void DrawLeafCloud(GeometryBatch batch, Vector2 c, float r, (ColorRgba Dark, ColorRgba Mid, ColorRgba Light) pal, uint h, byte alpha, int blobs)
    {
        byte sub = (byte)(alpha * 180 / 255);
        batch.FillCircle(c + new Vector2(0f, 8f), r * 0.8f, WithAlpha(pal.Dark, sub), 16);
        float squash = 0.75f + (Hash01(h, 4) * 0.3f);
        for (int i = 0; i < blobs; i++)
        {
            float a = (i / (float)blobs * MathF.Tau) + (Hash01(h, 100 + i) * 0.9f);
            float d = r * (0.35f + (Hash01(h, 110 + i) * 0.25f));
            Vector2 p = c + new Vector2(MathF.Cos(a) * d, MathF.Sin(a) * d * squash);
            batch.FillCircle(p, r * (0.5f + (Hash01(h, 120 + i) * 0.25f)), WithAlpha(i % 3 == 0 ? pal.Dark : pal.Mid, alpha), 14);
        }

        batch.FillCircle(c + new Vector2(0f, -r * 0.15f), r * 0.7f, WithAlpha(pal.Mid, alpha), 16);
        int highlights = 1 + (int)(Hash01(h, 130) * 3f);
        for (int i = 0; i < highlights; i++)
        {
            Vector2 p = c + new Vector2(-r * (0.15f + (Hash01(h, 140 + i) * 0.3f)), -r * (0.2f + (Hash01(h, 150 + i) * 0.35f)));
            batch.FillCircle(p, r * (0.18f + (Hash01(h, 160 + i) * 0.14f)), WithAlpha(pal.Light, alpha), 10);
        }
    }

    // Trunk with a lean, bark stripe and a root flare.
    private static void DrawTrunk(GeometryBatch batch, Vector2 pos, float width, float height, float lean, ColorRgba bark, uint h)
    {
        Vector2 baseL = new(pos.X - (width * 0.5f), pos.Y + 16f);
        Vector2 baseR = new(pos.X + (width * 0.5f), pos.Y + 16f);
        Vector2 topL = new(pos.X - (width * 0.4f) + lean, pos.Y + 16f - height);
        Vector2 topR = new(pos.X + (width * 0.4f) + lean, pos.Y + 16f - height);
        batch.FillTriangle(baseL, baseR, topR, bark);
        batch.FillTriangle(baseL, topR, topL, bark);
        batch.DrawLine(new Vector2(pos.X - (width * 0.2f), pos.Y + 14f), new Vector2(pos.X - (width * 0.15f) + lean, pos.Y + 18f - height), Shade(bark, 1.35f), 2f);
        batch.FillTriangle(baseL + new Vector2(-5f, 0f), baseL + new Vector2(4f, 0f), baseL + new Vector2(2f, -7f), Shade(bark, 0.8f));
        batch.FillTriangle(baseR + new Vector2(5f, 0f), baseR + new Vector2(-4f, 0f), baseR + new Vector2(-2f, -6f), Shade(bark, 0.8f));
        if (Hash01(h, 15) > 0.7f)
        {
            batch.FillCircle(new Vector2(pos.X + (lean * 0.5f), pos.Y + 4f - (height * 0.4f)), 2.5f, Shade(bark, 0.55f), 8); // knot
        }
    }

    // ── Rocks, ores and crystals ─────────────────────────────────────────────

    // An irregular rock: a hashed polygon with a lit top facet and a shaded base.
    private static void DrawRock(GeometryBatch batch, Vector2 pos, float r, ColorRgba body, uint h)
    {
        int n = 7 + (int)(Hash01(h, 2) * 3f);
        Span<Vector2> pts = stackalloc Vector2[10];
        Span<Vector2> shadow = stackalloc Vector2[10];
        float rot = Hash01(h, 3) * MathF.Tau;
        for (int i = 0; i < n; i++)
        {
            float a = rot + (i * (MathF.Tau / n));
            float rr = r * (0.78f + (Hash01(h, 20 + i) * 0.3f));
            pts[i] = pos + new Vector2(MathF.Cos(a) * rr, MathF.Sin(a) * rr * 0.82f);
            shadow[i] = pts[i] + new Vector2(4f, 6f);
        }

        batch.FillConvexPolygon(shadow[..n], new ColorRgba(0, 0, 0, 55));
        batch.FillConvexPolygon(pts[..n], Shade(body, 0.8f));
        for (int i = 0; i < n; i++)
        {
            pts[i] = pos + ((pts[i] - pos) * 0.78f) + new Vector2(-1.5f, -2.5f);
        }

        batch.FillConvexPolygon(pts[..n], body);
        batch.FillCircle(pos + new Vector2(-r * 0.3f, -r * 0.35f), r * 0.28f, new ColorRgba(255, 255, 255, 35), 8);
    }

    private static void DrawOreNuggets(GeometryBatch batch, Vector2 pos, float r, ColorRgba ore, uint h, bool dull)
    {
        int count = 3 + (int)(Hash01(h, 30) * 4f);
        for (int i = 0; i < count; i++)
        {
            float a = Hash01(h, 40 + i) * MathF.Tau;
            float d = r * Hash01(h, 50 + i) * 0.6f;
            Vector2 p = pos + new Vector2(MathF.Cos(a) * d, MathF.Sin(a) * d * 0.8f) + new Vector2(-1f, -2f);
            float size = r * (0.12f + (Hash01(h, 60 + i) * 0.16f));
            batch.FillCircle(p, size, Vary(ore, h, 70 + i, 0.12f), 5 + (i % 2));
            if (!dull && i % 2 == 0)
            {
                batch.FillCircle(p + new Vector2(-size * 0.35f, -size * 0.35f), size * 0.35f, new ColorRgba(255, 255, 255, 220), 4);
            }
        }
    }

    // Crystals as faceted shards of varying count, height and tilt growing out of a small base.
    private static void DrawCrystals(GeometryBatch batch, Vector2 pos, float r, ColorRgba body, ColorRgba tip, uint h, bool icy)
    {
        batch.FillCircle(new Vector2(pos.X + 2f, pos.Y + 6f), r * 0.8f, new ColorRgba(0, 0, 0, 55), segments: 8);
        batch.FillCircle(new Vector2(pos.X, pos.Y + 3f), r * 0.55f, icy ? new ColorRgba(200, 220, 236) : new ColorRgba(90, 84, 110), segments: 7);
        int shards = 3 + (int)(Hash01(h, 2) * 4f);
        for (int i = 0; i < shards; i++)
        {
            float spread = (i - ((shards - 1) * 0.5f)) / Math.Max(shards - 1, 1);
            float tilt = (spread * 0.9f) + ((Hash01(h, 10 + i) - 0.5f) * 0.4f);
            float height = r * (0.9f + (Hash01(h, 20 + i) * 0.9f)) * (1f - (MathF.Abs(spread) * 0.4f));
            float w = r * (0.22f + (Hash01(h, 30 + i) * 0.12f));
            Vector2 b = new(pos.X + (spread * r * 0.8f), pos.Y + 4f);
            Vector2 dir = new(MathF.Sin(tilt), -MathF.Cos(tilt));
            Vector2 side = new(-dir.Y * w, dir.X * w);
            Vector2 shoulder = b + (dir * height * 0.75f);
            Vector2 peak = b + (dir * height);
            ColorRgba c = Vary(body, h, 40 + i, 0.15f);
            batch.FillTriangle(b - side, b + side, shoulder + side, c);
            batch.FillTriangle(b - side, shoulder + side, shoulder - side, c);
            batch.FillTriangle(shoulder - side, shoulder + side, peak, Vary(tip, h, 50 + i, 0.1f));
            batch.DrawLine(b, peak, new ColorRgba(255, 255, 255, 120), 1.5f); // facet edge
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static uint Seed(in MapProp prop)
    {
        uint x = BitConverter.SingleToUInt32Bits(prop.WorldPositionX);
        uint y = BitConverter.SingleToUInt32Bits(prop.WorldPositionY);
        uint h = (x * 0x9E3779B1u) ^ (y * 0x85EBCA77u) ^ ((uint)prop.Type * 0xC2B2AE3Du);
        h ^= h >> 15;
        h *= 0x2C1B3C6Du;
        h ^= h >> 12;
        return h;
    }

    private static float Hash01(uint seed, int i) => AmbientAnimator.Hash01(seed, i);

    private static float Lean(uint h) => (Hash01(h, 1) - 0.5f) * 8f;

    private static ColorRgba Hex(uint hex) => new((byte)(hex >> 24), (byte)(hex >> 16), (byte)(hex >> 8), (byte)hex);

    private static ColorRgba WithAlpha(ColorRgba c, byte alpha) => new(c.R, c.G, c.B, (byte)(c.A * alpha / 255));

    // Brightness jitter of +/- amount plus a slight warm/cool shift, per instance.
    private static ColorRgba Vary(ColorRgba c, uint h, int i, float amount)
    {
        float k = 1f + ((Hash01(h, i) - 0.5f) * 2f * amount);
        float warm = (Hash01(h, i + 500) - 0.5f) * amount * 40f;
        return new ColorRgba(
            (byte)Math.Clamp((c.R * k) + warm, 0f, 255f),
            (byte)Math.Clamp(c.G * k, 0f, 255f),
            (byte)Math.Clamp((c.B * k) - warm, 0f, 255f),
            c.A);
    }

    private static ColorRgba Shade(ColorRgba c, float k) =>
        new((byte)Math.Clamp(c.R * k, 0f, 255f), (byte)Math.Clamp(c.G * k, 0f, 255f), (byte)Math.Clamp(c.B * k, 0f, 255f), c.A);
}
