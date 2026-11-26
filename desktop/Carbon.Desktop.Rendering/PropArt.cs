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

    /// <summary>Draws the ground layer of a prop (trunk, stem, rock body) with progressive damage cracks/notches and hit feedback.</summary>
    public static void DrawGround(
        GeometryBatch batch,
        in MapProp prop,
        Vector2 shakeOffset = default,
        float hitFlash = 0f)
    {
        Vector2 pos = prop.WorldPosition + shakeOffset;
        uint h = Seed(prop);
        ColorRgba primary = ApplyHitFlash(Hex(prop.ColorPrimary), hitFlash);
        ColorRgba secondary = ApplyHitFlash(Vary(Hex(prop.ColorSecondary), h, 1, 0.12f), hitFlash);
        float r = prop.Radius;
        float lean = Lean(h);
        float hpRatio = prop.Destructibility.IsDestructible
            ? Math.Clamp(prop.Destructibility.HealthPercentage, 0f, 1f)
            : 1f;
        float damageTilt = hpRatio < 0.99f ? (1f - hpRatio) * ((h & 1U) == 0 ? 5.5f : -5.5f) : 0f;

        switch (prop.Type)
        {
            case MapPropType.OakTree:
            case MapPropType.WillowTree:
            case MapPropType.CherryBlossomTree:
                {
                    float tw = prop.Type == MapPropType.WillowTree ? 16f : 13f + (Hash01(h, 2) * 4f);
                    batch.FillCircle(new Vector2(pos.X + 4f, pos.Y + 14f), r * 0.45f, s_shadow);
                    DrawTrunk(batch, pos, tw, 26f, lean + damageTilt, secondary, h);
                    if (prop.Type == MapPropType.CherryBlossomTree)
                    {
                        batch.FillCircle(new Vector2(pos.X, pos.Y + 10f), r * 0.65f, new ColorRgba(244, 114, 182, 60));
                    }

                    if (hpRatio < 0.99f)
                    {
                        DrawAxeNotch(batch, pos, tw, lean + (damageTilt * 0.45f), hpRatio, h);
                    }

                    break;
                }

            case MapPropType.BirchTree:
                batch.FillCircle(new Vector2(pos.X, pos.Y + 10f), r * 0.4f, s_shadow);
                DrawTrunk(batch, pos, 9f + (Hash01(h, 2) * 2f), 30f, lean + damageTilt, ApplyHitFlash(new ColorRgba(236, 232, 222), hitFlash), h);
                for (int i = 0; i < 3 + (int)(Hash01(h, 3) * 3f); i++)
                {
                    float y = pos.Y - 6f + (Hash01(h, 10 + i) * 24f);
                    float x = pos.X - 4f + (Hash01(h, 20 + i) * 5f) + ((lean + damageTilt) * (pos.Y + 16f - y) / 30f);
                    batch.FillRectangle(new RectangleF(x, y, 3f + (Hash01(h, 30 + i) * 3f), 2f), new ColorRgba(30, 41, 59));
                }

                if (hpRatio < 0.99f)
                {
                    DrawAxeNotch(batch, pos, 10f, lean + (damageTilt * 0.45f), hpRatio, h);
                }

                break;

            case MapPropType.PineTree:
            case MapPropType.SnowPine:
                batch.FillCircle(new Vector2(pos.X, pos.Y + 8f), r * 0.35f, s_shadow);
                DrawTrunk(batch, pos, 9f + (Hash01(h, 2) * 3f), 24f, (lean * 0.4f) + damageTilt, ApplyHitFlash(new ColorRgba(113, 63, 18), hitFlash), h);
                if (hpRatio < 0.99f)
                {
                    DrawAxeNotch(batch, pos, 10f, (lean * 0.4f) + (damageTilt * 0.45f), hpRatio, h);
                }

                break;

            case MapPropType.PalmTree:
                {
                    // A curved trunk: segments stepping sideways
                    batch.FillCircle(new Vector2(pos.X, pos.Y + 8f), r * 0.35f, new ColorRgba(0, 0, 0, 40));
                    float bend = ((Hash01(h, 2) - 0.5f) * 18f) + (damageTilt * 1.1f);
                    for (int i = 0; i < 6; i++)
                    {
                        float t = i / 6f;
                        float x = pos.X + (bend * t * t);
                        float y = pos.Y + 22f - (t * 36f);
                        batch.FillRectangle(new RectangleF(x - 4f, y - 6f, 8f, 7f), (i & 1) == 0 ? secondary : Shade(secondary, 0.85f));
                    }

                    if (hpRatio < 0.99f)
                    {
                        DrawAxeNotch(batch, pos + new Vector2(0f, 4f), 9f, bend * 0.15f, hpRatio, h);
                    }

                    break;
                }

            case MapPropType.AcaciaTree:
                {
                    batch.FillCircle(new Vector2(pos.X, pos.Y + 8f), r * 0.4f, s_shadow);
                    DrawTrunk(batch, pos, 9f, 28f, lean + damageTilt, secondary, h);
                    float fork = 10f + (Hash01(h, 3) * 6f);
                    batch.DrawLine(new Vector2(pos.X + lean + damageTilt, pos.Y - 12f), new Vector2(pos.X - fork + damageTilt, pos.Y - 22f), secondary, 5f);
                    batch.DrawLine(new Vector2(pos.X + lean + damageTilt, pos.Y - 12f), new Vector2(pos.X + fork + 2f + damageTilt, pos.Y - 25f), secondary, 5f);
                    if (hpRatio < 0.99f)
                    {
                        DrawAxeNotch(batch, pos, 9f, lean + (damageTilt * 0.45f), hpRatio, h);
                    }

                    break;
                }

            case MapPropType.DeadTree:
                {
                    batch.FillCircle(new Vector2(pos.X, pos.Y + 8f), r * 0.3f, s_shadow);
                    DrawTrunk(batch, pos, 10f, 32f, lean + damageTilt, primary, h);
                    int branches = 2 + (int)(Hash01(h, 3) * 3f);
                    for (int i = 0; i < branches; i++)
                    {
                        float y = pos.Y - 18f + (i * 7f);
                        float side = ((h >> i) & 1) == 0 ? -1f : 1f;
                        float len = 12f + (Hash01(h, 40 + i) * 10f);
                        Vector2 a = new(pos.X + lean + damageTilt, y);
                        Vector2 b = a + new Vector2(side * len, -6f - (Hash01(h, 50 + i) * 8f));
                        batch.DrawLine(a, b, secondary, 4f);
                        batch.DrawLine(b, b + new Vector2(side * 5f, -6f), secondary, 2.5f);
                    }

                    if (hpRatio < 0.99f)
                    {
                        DrawAxeNotch(batch, pos, 10f, lean + (damageTilt * 0.45f), hpRatio, h);
                    }

                    break;
                }

            case MapPropType.Cactus:
                {
                    batch.FillCircle(new Vector2(pos.X, pos.Y + 6f), r * 0.35f, new ColorRgba(0, 0, 0, 40));
                    float height = (34f + (Hash01(h, 2) * 14f)) * (0.72f + (0.28f * hpRatio));
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

                    if (Hash01(h, 9) > 0.7f && hpRatio > 0.5f)
                    {
                        batch.FillCircle(new Vector2(pos.X, pos.Y + 20f - height), 3.5f, new ColorRgba(244, 114, 182), 8); // flower
                    }

                    break;
                }

            case MapPropType.Bush:
                {
                    float scaleHp = 0.74f + (0.26f * hpRatio);
                    float rb = r * scaleHp;
                    int blobs = 3 + (int)(Hash01(h, 2) * 3f);
                    ColorRgba dark = Vary(primary, h, 3, 0.15f);
                    ColorRgba light = Vary(ApplyHitFlash(Hex(prop.ColorSecondary), hitFlash), h, 4, 0.15f);
                    for (int i = 0; i < blobs; i++)
                    {
                        float a = (i / (float)blobs * MathF.Tau) + Hash01(h, 10 + i);
                        Vector2 c = pos + new Vector2(MathF.Cos(a) * rb * 0.5f, (MathF.Sin(a) * rb * 0.3f) + 2f);
                        batch.FillCircle(c, rb * (0.55f + (Hash01(h, 20 + i) * 0.3f)), dark, 12);
                    }

                    batch.FillCircle(new Vector2(pos.X, pos.Y - 4f), rb * (0.8f + (Hash01(h, 5) * 0.25f)), light, 14);
                    if (Hash01(h, 6) > 0.6f && hpRatio > 0.45f)
                    {
                        ColorRgba berry = Hash01(h, 7) > 0.5f ? new ColorRgba(220, 38, 38) : new ColorRgba(96, 80, 200);
                        for (int i = 0; i < 4; i++)
                        {
                            batch.FillCircle(pos + new Vector2((Hash01(h, 60 + i) - 0.5f) * rb * 1.4f, (Hash01(h, 70 + i) - 0.6f) * rb), 2f, berry, 6);
                        }
                    }

                    break;
                }

            case MapPropType.MushroomCluster:
                MushroomArt.DrawCluster(batch, pos + new Vector2(0f, 6f), r * (0.85f + (0.45f * hpRatio)), h);
                break;

            case MapPropType.CopperVein:
            case MapPropType.IronVein:
            case MapPropType.GoldVein:
            case MapPropType.CoalDeposit:
            case MapPropType.SilverVein:
                {
                    float rockR = r * (0.82f + (0.18f * hpRatio));
                    DrawRock(batch, pos, rockR, Vary(primary, h, 2, 0.1f), h);
                    DrawOreNuggets(batch, pos, rockR, ApplyHitFlash(Hex(prop.ColorSecondary), hitFlash), h, prop.Type == MapPropType.CoalDeposit);
                    if (hpRatio < 0.99f)
                    {
                        DrawRockCracks(batch, pos, rockR, hpRatio, h, icy: false);
                    }

                    break;
                }

            case MapPropType.Boulder:
                {
                    float rockR = r * (0.80f + (0.20f * hpRatio));
                    DrawRock(batch, pos, rockR, Vary(primary, h, 2, 0.12f), h);
                    if (Hash01(h, 8) > 0.55f && hpRatio > 0.55f)
                    {
                        batch.FillCircle(pos + new Vector2(-rockR * 0.2f, -rockR * 0.45f), rockR * 0.35f, new ColorRgba(74, 124, 60, 200), 8);
                    }

                    if (hpRatio < 0.99f)
                    {
                        DrawRockCracks(batch, pos, rockR, hpRatio, h, icy: false);
                    }

                    break;
                }

            case MapPropType.CrystalCluster:
            case MapPropType.GlacialCrystal:
                DrawCrystals(batch, pos, r * (0.76f + (0.24f * hpRatio)), primary, secondary, h, prop.Type == MapPropType.GlacialCrystal, hpRatio);
                if (hpRatio < 0.99f)
                {
                    DrawRockCracks(batch, pos, r * 0.85f, hpRatio, h, icy: true);
                }

                break;

            case MapPropType.BasaltPillar:
                {
                    batch.FillCircle(new Vector2(pos.X, pos.Y + 8f), r * 0.85f, new ColorRgba(0, 0, 0, 60), segments: 6);
                    int columns = 2 + (int)(Hash01(h, 2) * 3f);
                    for (int i = 0; i < columns; i++)
                    {
                        Vector2 p = pos + new Vector2((i - ((columns - 1) * 0.5f)) * r * 0.55f, (Hash01(h, 10 + i) - 0.5f) * 6f);
                        float height = (10f + (Hash01(h, 20 + i) * 16f)) * (0.55f + (0.45f * hpRatio));
                        batch.FillRectangle(new RectangleF(p.X - (r * 0.28f), p.Y - height, r * 0.56f, height), Shade(primary, 0.8f));
                        batch.FillCircle(new Vector2(p.X, p.Y - height), r * 0.3f, secondary, segments: 6);
                    }

                    if (hpRatio < 0.99f)
                    {
                        DrawRockCracks(batch, pos, r, hpRatio, h, icy: false);
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

    /// <summary>
    /// Draws flat, non-blocking crushed stone rubble, cut tree stumps, or trimmed scrub when a natural prop has been harvested.
    /// </summary>
    public static void DrawDestroyedRemnant(GeometryBatch batch, in MapProp prop)
    {
        Vector2 pos = prop.WorldPosition;
        uint h = Seed(prop);
        ColorRgba primary = Hex(prop.ColorPrimary);
        ColorRgba secondary = Vary(Hex(prop.ColorSecondary), h, 1, 0.1f);

        switch (prop.Category)
        {
            case PropCategory.Flora when prop.HasCanopy || prop.Type == MapPropType.DeadTree:
                {
                    // Cut tree stump with heartwood core + scattered wood chips
                    float stumpR = 8.5f;
                    ColorRgba bark = prop.Type == MapPropType.BirchTree
                        ? new ColorRgba(214, 210, 200, 240)
                        : Shade(secondary, 0.78f);
                    batch.FillCircle(pos + new Vector2(2f, 12f), stumpR + 2.5f, new ColorRgba(0, 0, 0, 45), 10);
                    batch.FillRectangle(new RectangleF(pos.X - stumpR, pos.Y + 4f, stumpR * 2f, 10f), bark);
                    batch.FillCircle(pos + new Vector2(0f, 5f), stumpR, new ColorRgba(222, 184, 135, 245), 12);
                    batch.DrawCircle(pos + new Vector2(0f, 5f), stumpR, bark, 12, 1.5f);
                    batch.DrawCircle(pos + new Vector2(0f, 5f), stumpR * 0.55f, new ColorRgba(180, 130, 82, 200), 10, 1f);
                    for (int i = 0; i < 4; i++)
                    {
                        float a = Hash01(h, 80 + i) * MathF.Tau;
                        float d = 12f + (Hash01(h, 90 + i) * 7f);
                        Vector2 chip = pos + new Vector2(MathF.Cos(a) * d, 8f + (MathF.Sin(a) * d * 0.55f));
                        batch.FillRectangle(new RectangleF(chip.X - 2f, chip.Y - 1f, 4f, 2f), new ColorRgba(210, 168, 114, 215));
                    }

                    break;
                }

            case PropCategory.Flora:
                {
                    // Trimmed scrub / cut stalks
                    batch.FillCircle(pos + new Vector2(0f, 4f), 6f, new ColorRgba(0, 0, 0, 30), 8);
                    for (int i = 0; i < 4; i++)
                    {
                        float a = Hash01(h, 70 + i) * MathF.Tau;
                        Vector2 p = pos + new Vector2(MathF.Cos(a) * 6f, MathF.Sin(a) * 4f);
                        batch.FillCircle(p, 2.5f, Shade(primary, 0.7f), 6);
                    }

                    break;
                }

            case PropCategory.Mineral:
                {
                    // Shattered stone/ore rubble mound + dust shadow
                    ColorRgba stone = prop.Type is MapPropType.CrystalCluster or MapPropType.GlacialCrystal
                        ? new ColorRgba(120, 132, 152, 215)
                        : Shade(primary, 0.72f);
                    batch.FillCircle(pos + new Vector2(1f, 4f), 11f, new ColorRgba(0, 0, 0, 42), 10);
                    for (int i = 0; i < 5; i++)
                    {
                        float a = (i * (MathF.Tau / 5f)) + Hash01(h, 40 + i);
                        float d = 3f + (Hash01(h, 50 + i) * 6f);
                        Vector2 shardPos = pos + new Vector2(MathF.Cos(a) * d, MathF.Sin(a) * d * 0.7f);
                        float sr = 3f + (Hash01(h, 60 + i) * 2.5f);
                        batch.FillCircle(shardPos, sr, i == 0 ? secondary : Vary(stone, h, 70 + i, 0.14f), 6);
                    }

                    break;
                }

            case PropCategory.None:
            case PropCategory.Structure:
            case PropCategory.Decorative:
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
    public static void DrawCanopy(GeometryBatch batch, in MapProp prop, byte alpha, float swayX = 0f, float damageTilt = 0f)
    {
        uint h = Seed(prop);
        Vector2 pos = prop.WorldPosition + new Vector2(swayX + damageTilt, 0f);
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
    private static void DrawCrystals(
        GeometryBatch batch,
        Vector2 pos,
        float r,
        ColorRgba body,
        ColorRgba tip,
        uint h,
        bool icy,
        float hpRatio = 1f)
    {
        batch.FillCircle(new Vector2(pos.X + 2f, pos.Y + 6f), r * 0.8f, new ColorRgba(0, 0, 0, 55), segments: 8);
        batch.FillCircle(new Vector2(pos.X, pos.Y + 3f), r * 0.55f, icy ? new ColorRgba(200, 220, 236) : new ColorRgba(90, 84, 110), segments: 7);
        int baseShards = 3 + (int)(Hash01(h, 2) * 4f);
        int shards = Math.Max(2, (int)MathF.Ceiling(baseShards * (0.45f + (0.55f * hpRatio))));
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

    private static void DrawAxeNotch(GeometryBatch batch, Vector2 pos, float trunkWidth, float leanOffset, float hpRatio, uint h)
    {
        float severity = Math.Clamp(1f - hpRatio, 0.12f, 1f);
        float sideSign = (h & 1U) == 0 ? 1f : -1f;
        float yCenter = pos.Y + 5f;
        float outerX = pos.X + (leanOffset * 0.4f) + (sideSign * trunkWidth * 0.48f);
        float innerX = outerX - (sideSign * trunkWidth * (0.26f + (0.45f * severity)));
        float halfOpen = 2.4f + (severity * 3.8f);

        ColorRgba heartwood = new(226, 188, 138, 255);
        ColorRgba shadowCrease = new(78, 44, 22, 240);
        batch.FillTriangle(
            new Vector2(outerX, yCenter - halfOpen),
            new Vector2(outerX, yCenter + halfOpen),
            new Vector2(innerX, yCenter),
            heartwood);
        batch.DrawLine(new Vector2(outerX, yCenter), new Vector2(innerX, yCenter), shadowCrease, 1.6f);

        int chips = 2 + (int)(severity * 4f);
        for (int i = 0; i < chips; i++)
        {
            float dx = sideSign * (4f + (Hash01(h, 210 + i) * 10f));
            float dy = 13f + (Hash01(h, 220 + i) * 5f);
            batch.FillRectangle(new RectangleF(pos.X + dx, pos.Y + dy, 3f, 1.8f), heartwood);
        }
    }

    private static void DrawRockCracks(GeometryBatch batch, Vector2 pos, float r, float hpRatio, uint h, bool icy)
    {
        float damage = Math.Clamp(1f - hpRatio, 0.1f, 1f);
        int crackCount = hpRatio < 0.35f ? 5 : hpRatio < 0.70f ? 3 : 2;
        ColorRgba crackColor = icy ? new ColorRgba(240, 249, 255, 235) : new ColorRgba(16, 19, 26, 238);
        ColorRgba bevelColor = icy ? new ColorRgba(56, 189, 248, 175) : new ColorRgba(203, 213, 225, 110);
        float stroke = 1.4f + (damage * 1.4f);

        for (int i = 0; i < crackCount; i++)
        {
            float angle = (Hash01(h, 300 + i) * MathF.Tau) + (i * 1.1f);
            float reach = r * (0.38f + (0.46f * damage));
            Vector2 mid = pos + new Vector2(MathF.Cos(angle) * reach * 0.48f, MathF.Sin(angle) * reach * 0.36f);
            Vector2 tip = pos + new Vector2(MathF.Cos(angle + 0.32f) * reach, MathF.Sin(angle + 0.32f) * reach * 0.8f);
            batch.DrawLine(pos + new Vector2(0.8f, 0.8f), mid + new Vector2(0.8f, 0.8f), bevelColor, stroke);
            batch.DrawLine(pos, mid, crackColor, stroke);
            batch.DrawLine(mid, tip, crackColor, stroke * 0.8f);

            if (damage > 0.45f)
            {
                Vector2 fork = mid + new Vector2(MathF.Cos(angle - 0.55f) * reach * 0.45f, MathF.Sin(angle - 0.55f) * reach * 0.38f);
                batch.DrawLine(mid, fork, crackColor, stroke * 0.7f);
            }
        }

        // Chipped rubble flecks at the rock base
        int flecks = 2 + (int)(damage * 4f);
        for (int i = 0; i < flecks; i++)
        {
            float angle = Hash01(h, 340 + i) * MathF.Tau;
            float dist = r * (0.85f + (Hash01(h, 350 + i) * 0.35f));
            Vector2 shard = pos + new Vector2(MathF.Cos(angle) * dist, (MathF.Sin(angle) * dist * 0.65f) + 3f);
            batch.FillCircle(shard, 2.1f, new ColorRgba(100, 116, 139, 220), 5);
        }
    }

    private static ColorRgba ApplyHitFlash(ColorRgba c, float flash)
    {
        if (flash <= 0.001f)
        {
            return c;
        }

        float t = Math.Clamp(flash, 0f, 1f);
        return new ColorRgba(
            (byte)Math.Clamp(c.R + ((255 - c.R) * t), 0f, 255f),
            (byte)Math.Clamp(c.G + ((255 - c.G) * t), 0f, 255f),
            (byte)Math.Clamp(c.B + ((255 - c.B) * t), 0f, 255f),
            c.A);
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
