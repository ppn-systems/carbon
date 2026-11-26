// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Items.Catalog;

namespace Carbon.Desktop.Rendering.VectorArt;

/// <summary>
/// Renders modular character facial expressions, hairstyles, skin-tone overrides, and
/// equipped Head/Chest/Legs/Feet armor pieces directly onto an articulated rig's transformed skeleton bones.
/// </summary>
internal static class ModularCharacterOverlays
{
    private static readonly ColorRgba s_skinBaseSource = new(245, 198, 165, 255);
    private static readonly ColorRgba s_skinShadowSource = new(212, 155, 120, 255);
    private static readonly ColorRgba s_outline = new(20, 24, 36, 255);

    private static readonly ColorRgba[] s_skinBases =
    [
        new(245, 198, 165, 255), // Fair
        new(230, 169, 125, 255), // Warm Tan
        new(198, 134, 92, 255),  // Olive Bronze
        new(141, 85, 36, 255),   // Deep Umber
    ];

    private static readonly ColorRgba[] s_skinShadows =
    [
        new(212, 155, 120, 255),
        new(190, 130, 88, 255),
        new(158, 98, 62, 255),
        new(102, 58, 22, 255),
    ];

    private static readonly ColorRgba[] s_hairMains =
    [
        new(28, 32, 46, 255),    // Midnight Raven
        new(113, 63, 18, 255),   // Chestnut Brown
        new(234, 179, 8, 255),   // Golden Blonde
        new(203, 213, 225, 255), // Silver Ash
    ];

    private static readonly ColorRgba[] s_hairHighlights =
    [
        new(68, 76, 102, 255),
        new(161, 98, 7, 255),
        new(253, 224, 71, 255),
        new(241, 245, 249, 255),
    ];

    /// <summary>Substitutes placeholder peach skin colors in <c>base_hero.cvg.json</c> with the selected skin tone.</summary>
    public static ColorRgba ResolveModularColor(ColorRgba src, byte appearancePreset, bool isBaseHero)
    {
        if (!isBaseHero)
        {
            return src;
        }

        int tone = (appearancePreset >> 6) & 0x03;
        if (src.R == s_skinBaseSource.R && src.G == s_skinBaseSource.G && src.B == s_skinBaseSource.B)
        {
            return s_skinBases[tone];
        }

        if (src.R == s_skinShadowSource.R && src.G == s_skinShadowSource.G && src.B == s_skinShadowSource.B)
        {
            return s_skinShadows[tone];
        }

        return src;
    }

    public static void DrawEquippedBackCape(
        GeometryBatch batch,
        CompiledVectorRig rig,
        in BonePose capePose,
        Vector2 origin,
        float sx,
        float sy,
        bool isNorth,
        ushort chestId,
        float hitFlash)
    {
        if (chestId == 0)
        {
            return;
        }

        (ColorRgba mainCol, ColorRgba lightCol, ColorRgba darkCol) = (uint)chestId switch
        {
            StarterGameCatalog.IronChestplate => (new ColorRgba(220, 38, 38, 255), new ColorRgba(248, 113, 113, 255), new ColorRgba(127, 29, 29, 255)),
            StarterGameCatalog.CrystalRobe => (new ColorRgba(67, 56, 202, 255), new ColorRgba(56, 189, 248, 255), new ColorRgba(30, 27, 75, 255)),
            _ => (new ColorRgba(21, 128, 61, 255), new ColorRgba(74, 222, 128, 255), new ColorRgba(6, 78, 59, 255)),
        };

        Vector2 pivot = rig.GetBonePivot(VectorBoneId.Cape);
        Span<Vector2> outer = stackalloc Vector2[4];
        Span<Vector2> inner = stackalloc Vector2[4];
        float w = isNorth ? 8.4f : 7.4f;
        float tw = isNorth ? 6.2f : 5.4f;

        FillQuad(outer, pivot, capePose, origin, sx, sy, (-tw, -21.2f), (tw, -21.2f), (w, -4.4f), (-w, -4.4f));
        DrawOutlinedPoly(batch, outer, isNorth ? mainCol : darkCol, s_outline, 1.1f * MathF.Abs(sx), hitFlash);

        FillQuad(inner, pivot, capePose, origin, sx, sy, (-3.6f, -20.6f), (3.6f, -20.6f), (4.8f, -5.6f), (-4.8f, -5.6f));
        batch.FillConvexPolygon(inner, isNorth ? lightCol : mainCol);
    }

    public static void DrawLegAndFootArmor(
        GeometryBatch batch,
        CompiledVectorRig rig,
        in Span<BonePose> bones,
        Vector2 origin,
        float sx,
        float sy,
        bool isNorth,
        bool isSide,
        VisualEquipmentLoadout eq,
        float hitFlash)
    {
        if (eq.LegsId != 0)
        {
            (ColorRgba legMain, ColorRgba kneePlate) = eq.LegsId == StarterGameCatalog.IronLeggings
                ? (new ColorRgba(100, 116, 139, 255), new ColorRgba(203, 213, 225, 255))
                : (new ColorRgba(146, 64, 14, 255), new ColorRgba(217, 119, 6, 255));

            if (isSide)
            {
                DrawLegPlate(batch, rig.GetBonePivot(VectorBoneId.LegLeft), bones[(int)VectorBoneId.LegLeft], origin, sx, sy, -1.6f, legMain, kneePlate, hitFlash);
                DrawLegPlate(batch, rig.GetBonePivot(VectorBoneId.LegRight), bones[(int)VectorBoneId.LegRight], origin, sx, sy, 2.0f, legMain, kneePlate, hitFlash);
            }
            else
            {
                DrawLegPlate(batch, rig.GetBonePivot(VectorBoneId.LegLeft), bones[(int)VectorBoneId.LegLeft], origin, sx, sy, -3.2f, legMain, kneePlate, hitFlash);
                DrawLegPlate(batch, rig.GetBonePivot(VectorBoneId.LegRight), bones[(int)VectorBoneId.LegRight], origin, sx, sy, 3.2f, legMain, kneePlate, hitFlash);
            }
        }

        if (eq.FeetId != 0)
        {
            (ColorRgba bootCol, ColorRgba trimCol) = eq.FeetId == StarterGameCatalog.IronBoots
                ? (new ColorRgba(148, 163, 184, 255), new ColorRgba(226, 232, 240, 255))
                : (new ColorRgba(180, 83, 9, 255), new ColorRgba(250, 204, 21, 255));

            float lx = isSide ? -1.8f : -3.4f;
            float rx = isSide ? 2.6f : 3.4f;
            DrawBootPlate(batch, rig.GetBonePivot(VectorBoneId.LegLeft), bones[(int)VectorBoneId.LegLeft], origin, sx, sy, lx, bootCol, trimCol, isNorth, hitFlash);
            DrawBootPlate(batch, rig.GetBonePivot(VectorBoneId.LegRight), bones[(int)VectorBoneId.LegRight], origin, sx, sy, rx, bootCol, trimCol, isNorth, hitFlash);
        }
    }

    public static void DrawChestAndArmArmor(
        GeometryBatch batch,
        CompiledVectorRig rig,
        in Span<BonePose> bones,
        Vector2 origin,
        float sx,
        float sy,
        bool isNorth,
        bool isSide,
        ushort chestId,
        float hitFlash)
    {
        if (chestId == 0)
        {
            return;
        }

        (ColorRgba bodyCol, ColorRgba highCol, ColorRgba trimCol, ColorRgba gemCol) = (uint)chestId switch
        {
            StarterGameCatalog.IronChestplate => (
                new ColorRgba(148, 163, 184, 255),
                new ColorRgba(226, 232, 240, 255),
                new ColorRgba(250, 204, 21, 255),
                new ColorRgba(56, 189, 248, 255)),
            StarterGameCatalog.CrystalRobe => (
                new ColorRgba(79, 70, 229, 255),
                new ColorRgba(129, 140, 248, 255),
                new ColorRgba(250, 204, 21, 255),
                new ColorRgba(34, 211, 238, 255)),
            _ => (
                new ColorRgba(146, 64, 14, 255),
                new ColorRgba(217, 119, 6, 255),
                new ColorRgba(245, 158, 11, 255),
                new ColorRgba(74, 222, 128, 255)),
        };

        Vector2 bodyPivot = rig.GetBonePivot(VectorBoneId.Body);
        ref readonly BonePose bodyPose = ref bones[(int)VectorBoneId.Body];

        // Armored chestplate / robe skirt
        Span<Vector2> plate = stackalloc Vector2[4];
        float bottomY = chestId == StarterGameCatalog.CrystalRobe ? -7.2f : -10.8f;
        float bottomW = chestId == StarterGameCatalog.CrystalRobe ? 6.6f : 5.5f;
        FillQuad(plate, bodyPivot, bodyPose, origin, sx, sy, (-6.7f, -21.3f), (6.7f, -21.3f), (bottomW, bottomY), (-bottomW, bottomY));
        DrawOutlinedPoly(batch, plate, bodyCol, s_outline, 1.1f * MathF.Abs(sx), hitFlash);

        if (!isNorth)
        {
            FillQuad(plate, bodyPivot, bodyPose, origin, sx, sy, (-4.6f, -20.2f), (4.6f, -20.2f), (3.5f, -13.2f), (-3.5f, -13.2f));
            batch.FillConvexPolygon(plate, highCol);

            // Belt + Chest Crest Gem
            FillQuad(plate, bodyPivot, bodyPose, origin, sx, sy, (-5.7f, -12.0f), (5.7f, -12.0f), (5.7f, -9.8f), (-5.7f, -9.8f));
            batch.FillConvexPolygon(plate, new ColorRgba(69, 26, 3, 255));
            Vector2 gemPos = Transform(new Vector2(0f, -16.2f), bodyPivot, bodyPose, origin, sx, sy);
            batch.FillCircle(gemPos, 2.2f * MathF.Abs(sx), s_outline, 10);
            batch.FillCircle(gemPos, 1.6f * MathF.Abs(sx), gemCol, 10);
        }

        // Shoulder Pauldrons on Front & Back Arm
        float absS = MathF.Abs(sx);
        Vector2 armFPivot = rig.GetBonePivot(VectorBoneId.ArmFront);
        ref readonly BonePose armFPose = ref bones[(int)VectorBoneId.ArmFront];
        Vector2 pauldronPos = Transform(new Vector2(isSide ? 2.2f : 6.4f, -19.5f), armFPivot, armFPose, origin, sx, sy);
        batch.FillCircle(pauldronPos, 3.2f * absS, s_outline, 12);
        batch.FillCircle(pauldronPos, 2.4f * absS, trimCol, 12);

        if (!isSide)
        {
            Vector2 armBPivot = rig.GetBonePivot(VectorBoneId.ArmBack);
            ref readonly BonePose armBPose = ref bones[(int)VectorBoneId.ArmBack];
            Vector2 backPauldron = Transform(new Vector2(-6.4f, -19.5f), armBPivot, armBPose, origin, sx, sy);
            batch.FillCircle(backPauldron, 3.0f * absS, s_outline, 12);
            batch.FillCircle(backPauldron, 2.2f * absS, highCol, 12);
        }
    }

    public static void DrawFaceAndHair(
        GeometryBatch batch,
        CompiledVectorRig rig,
        in BonePose headPose,
        Vector2 origin,
        float sx,
        float sy,
        bool isSouth,
        bool isSide,
        bool isNorth,
        byte appearancePreset,
        ushort equippedHeadId,
        float hitFlash)
    {
        Vector2 headPivot = rig.GetBonePivot(VectorBoneId.Head);
        float absS = MathF.Abs(sx);
        int hairStyle = appearancePreset & 0x03;
        int hairColorIdx = (appearancePreset >> 2) & 0x03;
        int faceStyle = (appearancePreset >> 4) & 0x03;
        ColorRgba hairMain = s_hairMains[hairColorIdx];
        ColorRgba hairHi = s_hairHighlights[hairColorIdx];

        // 1. Facial eyes & brows (skipped on North or full Knight great-helm with closed visor)
        bool closedVisor = equippedHeadId == StarterGameCatalog.IronHelmet;
        if (!isNorth && !closedVisor)
        {
            ColorRgba irisColor = faceStyle switch
            {
                1 => new ColorRgba(220, 38, 38, 255),  // Fierce Crimson
                2 => new ColorRgba(16, 185, 129, 255), // Calm Emerald
                3 => new ColorRgba(56, 189, 248, 255), // Mystic Cyan
                _ => new ColorRgba(30, 58, 138, 255),  // Cheerful Cobalt
            };
            float eyeH = faceStyle == 2 ? 1.15f : 1.55f;
            float browTilt = faceStyle == 1 ? 0.8f : -0.2f;

            if (isSouth)
            {
                DrawEye(batch, Transform(new Vector2(-2.5f, -26.0f), headPivot, headPose, origin, sx, sy), 1.3f * absS, eyeH * absS, irisColor, hitFlash);
                DrawEye(batch, Transform(new Vector2(2.5f, -26.0f), headPivot, headPose, origin, sx, sy), 1.3f * absS, eyeH * absS, irisColor, hitFlash);

                Vector2 bl0 = Transform(new Vector2(-3.8f, -28.0f - browTilt), headPivot, headPose, origin, sx, sy);
                Vector2 bl1 = Transform(new Vector2(-1.2f, -27.8f + browTilt), headPivot, headPose, origin, sx, sy);
                Vector2 br0 = Transform(new Vector2(1.2f, -27.8f + browTilt), headPivot, headPose, origin, sx, sy);
                Vector2 br1 = Transform(new Vector2(3.8f, -28.0f - browTilt), headPivot, headPose, origin, sx, sy);
                batch.DrawLine(bl0, bl1, hairMain, MathF.Max(1f, 0.95f * absS));
                batch.DrawLine(br0, br1, hairMain, MathF.Max(1f, 0.95f * absS));
            }
            else if (isSide)
            {
                DrawEye(batch, Transform(new Vector2(3.5f, -26.0f), headPivot, headPose, origin, sx, sy), 1.15f * absS, eyeH * absS, irisColor, hitFlash);
            }
        }

        // 2. Hair styles (0: Tousled Short, 1: Spiky Hero, 2: Flowing Long, 3: Side-Swept Bob)
        if (closedVisor)
        {
            return;
        }

        Span<Vector2> poly = stackalloc Vector2[4];
        if (isNorth)
        {
            Vector2 domeCenter = Transform(new Vector2(0f, -26.8f), headPivot, headPose, origin, sx, sy);
            batch.FillCircle(domeCenter, 6.9f * absS, s_outline, 14);
            batch.FillCircle(domeCenter, 6.0f * absS, ApplyFlash(hairMain, hitFlash), 14);

            if (hairStyle == 1)
            {
                Span<Vector2> tri = stackalloc Vector2[3];
                FillTri(tri, headPivot, headPose, origin, sx, sy, (-6.2f, -29.2f), (-2.8f, -35.6f), (0.2f, -29.8f));
                DrawOutlinedPoly(batch, tri, hairMain, s_outline, 0.85f * absS, hitFlash);
                FillTri(tri, headPivot, headPose, origin, sx, sy, (-2.0f, -29.8f), (0.6f, -36.4f), (3.4f, -29.4f));
                DrawOutlinedPoly(batch, tri, hairHi, s_outline, 0.85f * absS, hitFlash);
                FillTri(tri, headPivot, headPose, origin, sx, sy, (2.0f, -29.4f), (5.6f, -34.6f), (6.2f, -28.8f));
                DrawOutlinedPoly(batch, tri, hairMain, s_outline, 0.85f * absS, hitFlash);
            }
            else if (hairStyle == 2)
            {
                FillQuad(poly, headPivot, headPose, origin, sx, sy, (-6.4f, -26.5f), (6.4f, -26.5f), (5.2f, -18.2f), (-5.2f, -18.2f));
                DrawOutlinedPoly(batch, poly, hairHi, s_outline, 0.9f * absS, hitFlash);
            }
            else
            {
                FillQuad(poly, headPivot, headPose, origin, sx, sy, (-5.4f, -25.8f), (5.4f, -25.8f), (4.2f, -21.2f), (-4.2f, -21.2f));
                DrawOutlinedPoly(batch, poly, hairMain, s_outline, 0.85f * absS, hitFlash);
            }
        }

        if (isSouth || isSide)
        {
            if (hairStyle == 1)
            {
                // Spiky bangs + crown tufts
                Span<Vector2> tri = stackalloc Vector2[3];
                FillTri(tri, headPivot, headPose, origin, sx, sy, (-6.8f, -29.0f), (-3.2f, -35.8f), (0.0f, -29.4f));
                DrawOutlinedPoly(batch, tri, hairMain, s_outline, 0.9f * absS, hitFlash);
                FillTri(tri, headPivot, headPose, origin, sx, sy, (-2.2f, -29.4f), (0.5f, -36.5f), (3.4f, -29.2f));
                DrawOutlinedPoly(batch, tri, hairHi, s_outline, 0.9f * absS, hitFlash);
                FillTri(tri, headPivot, headPose, origin, sx, sy, (1.8f, -29.2f), (5.8f, -34.8f), (6.8f, -28.8f));
                DrawOutlinedPoly(batch, tri, hairMain, s_outline, 0.9f * absS, hitFlash);
            }
            else if (hairStyle == 2)
            {
                // Long side-locks framing cheeks + crown
                FillQuad(poly, headPivot, headPose, origin, sx, sy, (-6.8f, -29.0f), (6.8f, -29.0f), (5.4f, -33.2f), (-5.4f, -33.2f));
                DrawOutlinedPoly(batch, poly, hairMain, s_outline, 0.9f * absS, hitFlash);
                if (isSouth)
                {
                    FillQuad(poly, headPivot, headPose, origin, sx, sy, (-7.2f, -28.8f), (-5.2f, -28.8f), (-5.6f, -19.5f), (-7.4f, -20.5f));
                    DrawOutlinedPoly(batch, poly, hairHi, s_outline, 0.8f * absS, hitFlash);
                    FillQuad(poly, headPivot, headPose, origin, sx, sy, (5.2f, -28.8f), (7.2f, -28.8f), (7.4f, -20.5f), (5.6f, -19.5f));
                    DrawOutlinedPoly(batch, poly, hairHi, s_outline, 0.8f * absS, hitFlash);
                }
            }
            else if (hairStyle == 3)
            {
                // Asymmetric side-swept bob
                FillQuad(poly, headPivot, headPose, origin, sx, sy, (-6.6f, -28.6f), (6.6f, -29.6f), (4.6f, -33.6f), (-5.2f, -32.6f));
                DrawOutlinedPoly(batch, poly, hairHi, s_outline, 0.9f * absS, hitFlash);
            }
            else
            {
                // Classic tousled adventurer crop
                FillQuad(poly, headPivot, headPose, origin, sx, sy, (-6.6f, -28.8f), (6.6f, -28.8f), (5.2f, -33.0f), (-5.2f, -33.0f));
                DrawOutlinedPoly(batch, poly, hairMain, s_outline, 0.9f * absS, hitFlash);
            }
        }
    }

    public static void DrawHeadArmor(
        GeometryBatch batch,
        CompiledVectorRig rig,
        in BonePose headgearPose,
        Vector2 origin,
        float sx,
        float sy,
        bool isSouth,
        bool isNorth,
        ushort headId,
        float hitFlash)
    {
        if (headId == 0)
        {
            return;
        }

        Vector2 pivot = rig.GetBonePivot(VectorBoneId.Headgear);
        float absS = MathF.Abs(sx);
        Span<Vector2> poly = stackalloc Vector2[4];

        if (headId == StarterGameCatalog.LeatherCap)
        {
            FillQuad(poly, pivot, headgearPose, origin, sx, sy, (-7.6f, -28.6f), (7.6f, -28.6f), (2.2f, -37.2f), (-4.0f, -35.8f));
            DrawOutlinedPoly(batch, poly, new ColorRgba(21, 128, 61, 255), s_outline, 1.1f * absS, hitFlash);
            // Feather plume
            FillQuad(poly, pivot, headgearPose, origin, sx, sy, (2.6f, -32.6f), (7.8f, -37.8f), (8.4f, -35.2f), (4.0f, -31.2f));
            DrawOutlinedPoly(batch, poly, new ColorRgba(250, 204, 21, 255), s_outline, 0.8f * absS, hitFlash);
        }
        else if (headId == StarterGameCatalog.IronHelmet)
        {
            FillQuad(poly, pivot, headgearPose, origin, sx, sy, (-7.5f, -28.2f), (7.5f, -28.2f), (6.4f, -34.8f), (-6.4f, -34.8f));
            DrawOutlinedPoly(batch, poly, new ColorRgba(203, 213, 225, 255), s_outline, 1.2f * absS, hitFlash);
            if (isNorth)
            {
                FillQuad(poly, pivot, headgearPose, origin, sx, sy, (-6.8f, -28.4f), (6.8f, -28.4f), (6.2f, -23.0f), (-6.2f, -23.0f));
                DrawOutlinedPoly(batch, poly, new ColorRgba(148, 163, 184, 255), s_outline, 1.0f * absS, hitFlash);
            }
            else
            {
                FillQuad(poly, pivot, headgearPose, origin, sx, sy, (-4.8f, -28.2f), (4.8f, -28.2f), (4.8f, -26.4f), (-4.8f, -26.4f));
                batch.FillConvexPolygon(poly, s_outline);
                FillQuad(poly, pivot, headgearPose, origin, sx, sy, (-3.4f, -27.7f), (3.4f, -27.7f), (3.4f, -26.9f), (-3.4f, -26.9f));
                batch.FillConvexPolygon(poly, new ColorRgba(125, 211, 252, 255));
            }

            // Crimson crest plume
            FillQuad(poly, pivot, headgearPose, origin, sx, sy, (-2.0f, -34.4f), (2.2f, -34.4f), (0.6f, -39.6f), (-2.4f, -37.6f));
            DrawOutlinedPoly(batch, poly, new ColorRgba(220, 38, 38, 255), s_outline, 0.9f * absS, hitFlash);

            if (isSouth)
            {
                Span<Vector2> wing = stackalloc Vector2[3];
                FillTri(wing, pivot, headgearPose, origin, sx, sy, (-8.8f, -29.6f), (-6.4f, -32.2f), (-6.8f, -27.8f));
                DrawOutlinedPoly(batch, wing, new ColorRgba(250, 204, 21, 255), s_outline, 0.8f * absS, hitFlash);
                FillTri(wing, pivot, headgearPose, origin, sx, sy, (8.8f, -29.6f), (6.8f, -27.8f), (6.4f, -32.2f));
                DrawOutlinedPoly(batch, wing, new ColorRgba(250, 204, 21, 255), s_outline, 0.8f * absS, hitFlash);
            }
        }
        else if (headId == StarterGameCatalog.CrystalCrown)
        {
            FillQuad(poly, pivot, headgearPose, origin, sx, sy, (-6.8f, -29.6f), (6.8f, -29.6f), (6.6f, -31.8f), (-6.6f, -31.8f));
            DrawOutlinedPoly(batch, poly, new ColorRgba(250, 204, 21, 255), s_outline, 0.9f * absS, hitFlash);

            Span<Vector2> spire = stackalloc Vector2[3];
            FillTri(spire, pivot, headgearPose, origin, sx, sy, (-2.4f, -31.6f), (0.0f, -38.8f), (2.4f, -31.6f));
            DrawOutlinedPoly(batch, spire, new ColorRgba(34, 211, 238, 255), s_outline, 0.9f * absS, hitFlash);
            Vector2 gem = Transform(new Vector2(0f, -34.2f), pivot, headgearPose, origin, sx, sy);
            batch.FillCircle(gem, 1.5f * absS, new ColorRgba(207, 250, 254, 255), 8);
        }
    }

    private static void DrawLegPlate(
        GeometryBatch batch,
        Vector2 pivot,
        in BonePose pose,
        Vector2 origin,
        float sx,
        float sy,
        float legX,
        ColorRgba main,
        ColorRgba knee,
        float hitFlash)
    {
        float absS = MathF.Abs(sx);
        Span<Vector2> poly = stackalloc Vector2[4];
        FillQuad(poly, pivot, pose, origin, sx, sy, (legX - 2.0f, -9.8f), (legX + 2.0f, -9.8f), (legX + 2.1f, -3.8f), (legX - 2.1f, -3.8f));
        DrawOutlinedPoly(batch, poly, main, s_outline, 0.9f * absS, hitFlash);
        Vector2 kp = Transform(new Vector2(legX, -5.8f), pivot, pose, origin, sx, sy);
        batch.FillCircle(kp, 1.6f * absS, knee, 8);
    }

    private static void DrawBootPlate(
        GeometryBatch batch,
        Vector2 pivot,
        in BonePose pose,
        Vector2 origin,
        float sx,
        float sy,
        float legX,
        ColorRgba bootCol,
        ColorRgba trimCol,
        bool isNorth,
        float hitFlash)
    {
        float absS = MathF.Abs(sx);
        Vector2 footPos = Transform(new Vector2(legX, -1.2f), pivot, pose, origin, sx, sy);
        batch.FillCircle(footPos, 2.5f * absS, s_outline, 10);
        batch.FillCircle(footPos, 1.9f * absS, ApplyFlash(bootCol, hitFlash), 10);
        if (!isNorth)
        {
            Vector2 cuffPos = Transform(new Vector2(legX, -3.5f), pivot, pose, origin, sx, sy);
            batch.FillCircle(cuffPos, 1.5f * absS, ApplyFlash(trimCol, hitFlash), 8);
        }
    }

    private static void DrawEye(GeometryBatch batch, Vector2 center, float rx, float ry, ColorRgba iris, float hitFlash)
    {
        float r = MathF.Max(rx, ry);
        batch.FillCircle(center, r * 1.15f, s_outline, 10);
        batch.FillCircle(center, r * 0.95f, new ColorRgba(248, 250, 252, 255), 10);
        batch.FillCircle(center, r * 0.65f, ApplyFlash(iris, hitFlash), 10);
        batch.FillCircle(center + new Vector2(-r * 0.25f, -r * 0.28f), MathF.Max(0.8f, r * 0.30f), ColorRgba.White, 6);
    }

    private static void DrawOutlinedPoly(GeometryBatch batch, Span<Vector2> pts, ColorRgba fill, ColorRgba outline, float stroke, float hitFlash)
    {
        for (int i = 0; i < pts.Length; i++)
        {
            batch.DrawLine(pts[i], pts[(i + 1) % pts.Length], outline, stroke);
        }

        batch.FillConvexPolygon(pts, ApplyFlash(fill, hitFlash));
    }

    private static void FillQuad(
        Span<Vector2> quad,
        Vector2 pivot,
        in BonePose bone,
        Vector2 origin,
        float sx,
        float sy,
        (float X, float Y) p0,
        (float X, float Y) p1,
        (float X, float Y) p2,
        (float X, float Y) p3)
    {
        quad[0] = Transform(new Vector2(p0.X, p0.Y), pivot, in bone, origin, sx, sy);
        quad[1] = Transform(new Vector2(p1.X, p1.Y), pivot, in bone, origin, sx, sy);
        quad[2] = Transform(new Vector2(p2.X, p2.Y), pivot, in bone, origin, sx, sy);
        quad[3] = Transform(new Vector2(p3.X, p3.Y), pivot, in bone, origin, sx, sy);
    }

    private static void FillTri(
        Span<Vector2> tri,
        Vector2 pivot,
        in BonePose bone,
        Vector2 origin,
        float sx,
        float sy,
        (float X, float Y) p0,
        (float X, float Y) p1,
        (float X, float Y) p2)
    {
        tri[0] = Transform(new Vector2(p0.X, p0.Y), pivot, in bone, origin, sx, sy);
        tri[1] = Transform(new Vector2(p1.X, p1.Y), pivot, in bone, origin, sx, sy);
        tri[2] = Transform(new Vector2(p2.X, p2.Y), pivot, in bone, origin, sx, sy);
    }

    private static Vector2 Transform(Vector2 local, Vector2 bonePivot, in BonePose bone, Vector2 origin, float sx, float sy)
    {
        float cosB = MathF.Cos(bone.RotRad);
        float sinB = MathF.Sin(bone.RotRad);
        float lx = (local.X - bonePivot.X) * bone.ScaleX;
        float ly = (local.Y - bonePivot.Y) * bone.ScaleY;
        float rx = bonePivot.X + (lx * cosB) - (ly * sinB) + bone.Dx;
        float ry = bonePivot.Y + (lx * sinB) + (ly * cosB) + bone.Dy;
        return new Vector2(origin.X + (rx * sx), origin.Y + (ry * sy));
    }

    private static ColorRgba ApplyFlash(ColorRgba src, float flash)
    {
        if (flash <= 0.001f)
        {
            return src;
        }

        float f = Math.Clamp(flash, 0f, 1f);
        byte r = (byte)Math.Clamp(src.R + ((255f - src.R) * f), 0f, 255f);
        byte g = (byte)Math.Clamp(src.G + ((220f - src.G) * f), 0f, 255f);
        byte b = (byte)Math.Clamp(src.B + ((220f - src.B) * f), 0f, 255f);
        return new ColorRgba(r, g, b, src.A);
    }
}
