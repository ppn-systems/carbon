// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Desktop.Abstractions;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Items.Catalog;

namespace Carbon.Desktop.Rendering.Effects;

/// <summary>
/// Renders action-specific combat and harvesting VFX ribbons (tapered melee slash arcs,
/// overhead woodcutter cleaves, and mining shockwave sparks) synchronized with vector rig swings.
/// </summary>
internal static class ActionVfxRenderer
{
    /// <summary>Draws active player or entity swing trails in world space.</summary>
    public static void DrawActionTrail(
        GeometryBatch batch,
        Vector2 origin,
        ClientActionKind kind,
        Vector2 direction,
        float progress,
        byte comboStep,
        uint itemId = 0U)
    {
        if (kind == ClientActionKind.None || progress >= 1f)
        {
            return;
        }

        Vector2 dir = direction.LengthSquared() > 1e-4f ? Vector2.Normalize(direction) : Vector2.UnitY;
        float baseAngle = MathF.Atan2(dir.Y, dir.X);
        float fade = 1f - MathF.Pow(progress, 1.8f);
        byte alpha = (byte)Math.Clamp(fade * 235f, 0f, 255f);

        switch (kind)
        {
            case ClientActionKind.SwordSlash:
                DrawSwordCleaveRibbon(batch, origin + new Vector2(0f, -6f), baseAngle, progress, comboStep, alpha);
                break;

            case ClientActionKind.AxeChop:
                DrawAxeTimberCleave(batch, origin + new Vector2(0f, -6f), dir, baseAngle, progress, alpha);
                break;

            case ClientActionKind.PickaxeMine:
                DrawPickaxeImpactBurst(
                    batch,
                    origin + new Vector2(0f, -6f),
                    dir,
                    baseAngle,
                    progress,
                    alpha,
                    isCrystalPick: itemId == StarterGameCatalog.CrystalPickaxe);
                break;

            case ClientActionKind.MonsterStrike:
                DrawMonsterStrikeArc(batch, origin + new Vector2(0f, -4f), baseAngle, progress, alpha);
                break;

            case ClientActionKind.None:
            case ClientActionKind.ConsumeItem:
            case ClientActionKind.PlaceStructure:
            default:
                break;
        }
    }

    private static void DrawSwordCleaveRibbon(
        GeometryBatch batch,
        Vector2 origin,
        float baseAngle,
        float progress,
        byte comboStep,
        byte alpha)
    {
        const int steps = 12;
        float span = 2.05f;
        float dirSign = (comboStep & 1) == 0 ? 1f : -1f;
        float startAngle = baseAngle - (dirSign * span * 0.5f);
        float headT = Math.Clamp(progress * 1.35f, 0.1f, 1f);
        float tailT = Math.Clamp(headT - 0.62f, 0f, headT);

        ColorRgba outerRim = new(248, 250, 252, alpha);
        ColorRgba midTrail = new(125, 211, 252, (byte)(alpha * 0.85f));
        ColorRgba innerGlow = new(56, 189, 248, (byte)(alpha * 0.48f));

        Vector2 prevOut = Vector2.Zero;
        Vector2 prevMid = Vector2.Zero;
        Vector2 prevIn = Vector2.Zero;
        bool hasPrev = false;

        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            if (t < tailT || t > headT)
            {
                continue;
            }

            float localNorm = (t - tailT) / MathF.Max(0.05f, headT - tailT);
            float taper = MathF.Sin(localNorm * MathF.PI * 0.72f);
            float ang = startAngle + (dirSign * span * t);
            Vector2 radial = new(MathF.Cos(ang), MathF.Sin(ang));

            float outerR = 36f + (MathF.Sin(t * MathF.PI) * 10f);
            float width = 4f + (taper * 12f);
            Vector2 pOut = origin + (radial * outerR);
            Vector2 pMid = origin + (radial * (outerR - (width * 0.45f)));
            Vector2 pIn = origin + (radial * MathF.Max(12f, outerR - width));

            if (hasPrev)
            {
                batch.FillTriangle(prevOut, pOut, pMid, midTrail);
                batch.FillTriangle(prevOut, pMid, prevMid, midTrail);
                batch.FillTriangle(prevMid, pMid, pIn, innerGlow);
                batch.FillTriangle(prevMid, pIn, prevIn, innerGlow);
                batch.DrawLine(prevOut, pOut, outerRim, 2.2f);
            }

            prevOut = pOut;
            prevMid = pMid;
            prevIn = pIn;
            hasPrev = true;
        }
    }

    private static void DrawAxeTimberCleave(
        GeometryBatch batch,
        Vector2 origin,
        Vector2 dir,
        float baseAngle,
        float progress,
        byte alpha)
    {
        const int steps = 8;
        float span = 1.35f;
        float startAngle = baseAngle - 0.85f;
        float headT = Math.Clamp(progress * 1.4f, 0.08f, 1f);

        ColorRgba bladeArc = new(254, 243, 199, alpha);
        ColorRgba woodWind = new(245, 158, 11, (byte)(alpha * 0.78f));

        Vector2 prevOut = Vector2.Zero;
        Vector2 prevIn = Vector2.Zero;
        bool hasPrev = false;

        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            if (t > headT)
            {
                break;
            }

            float ang = startAngle + (span * t);
            Vector2 radial = new(MathF.Cos(ang), MathF.Sin(ang));
            float outerR = 30f + (MathF.Sin(t * MathF.PI) * 6f);
            float innerR = outerR - (5f + (MathF.Sin(t * MathF.PI) * 9f));

            Vector2 pOut = origin + (radial * outerR);
            Vector2 pIn = origin + (radial * innerR);
            if (hasPrev)
            {
                batch.FillTriangle(prevOut, pOut, pIn, woodWind);
                batch.FillTriangle(prevOut, pIn, prevIn, woodWind);
                batch.DrawLine(prevOut, pOut, bladeArc, 2f);
            }

            prevOut = pOut;
            prevIn = pIn;
            hasPrev = true;
        }

        if (progress > 0.35f)
        {
            float impFrac = Math.Clamp((progress - 0.35f) / 0.65f, 0f, 1f);
            Vector2 hitCenter = origin + (dir * 26f);
            byte ringAlpha = (byte)((1f - impFrac) * 195f);
            batch.DrawCircle(hitCenter, 6f + (impFrac * 16f), new ColorRgba(253, 224, 71, ringAlpha), 14, 2f);
        }
    }

    private static void DrawPickaxeImpactBurst(
        GeometryBatch batch,
        Vector2 origin,
        Vector2 dir,
        float baseAngle,
        float progress,
        byte alpha,
        bool isCrystalPick)
    {
        ColorRgba primary = isCrystalPick
            ? new ColorRgba(103, 232, 249, alpha)
            : new ColorRgba(253, 224, 71, alpha);
        ColorRgba secondary = isCrystalPick
            ? new ColorRgba(224, 242, 254, alpha)
            : new ColorRgba(249, 115, 22, alpha);

        Vector2 apex = origin + (dir * (18f + (MathF.Sin(MathF.Min(1f, progress * 1.4f) * MathF.PI * 0.5f) * 18f)));
        Vector2 back = origin + (dir * 10f);
        Vector2 perp = new(-dir.Y, dir.X);
        batch.FillTriangle(apex, back + (perp * 6f), back - (perp * 6f), new ColorRgba(primary.R, primary.G, primary.B, (byte)(alpha * 0.45f)));
        batch.DrawLine(back, apex, secondary, 2.5f);

        if (progress >= 0.32f)
        {
            float impFrac = Math.Clamp((progress - 0.32f) / 0.68f, 0f, 1f);
            byte sparkAlpha = (byte)((1f - impFrac) * 240f);
            ColorRgba sparkColor = new(secondary.R, secondary.G, secondary.B, sparkAlpha);
            batch.DrawCircle(apex, 5f + (impFrac * 18f), new ColorRgba(primary.R, primary.G, primary.B, sparkAlpha), 12, 1.8f);

            for (int k = -2; k <= 2; k++)
            {
                float a = baseAngle + (k * 0.48f);
                Vector2 ray = new(MathF.Cos(a), MathF.Sin(a));
                float r0 = 4f + (impFrac * 8f);
                float r1 = 10f + (impFrac * 20f);
                batch.DrawLine(apex + (ray * r0), apex + (ray * r1), sparkColor, 1.8f);
            }
        }
    }

    private static void DrawMonsterStrikeArc(
        GeometryBatch batch,
        Vector2 origin,
        float baseAngle,
        float progress,
        byte alpha)
    {
        ColorRgba clawColor = new(248, 113, 113, alpha);
        ColorRgba core = new(254, 226, 226, (byte)(alpha * 0.9f));
        float span = 1.4f;
        float start = baseAngle - (span * 0.5f);
        float headT = Math.Clamp(progress * 1.3f, 0.1f, 1f);
        Vector2 prev = Vector2.Zero;
        bool hasPrev = false;

        for (int i = 0; i <= 7; i++)
        {
            float t = i / 7f;
            if (t > headT)
            {
                break;
            }

            float ang = start + (span * t);
            Vector2 p = origin + (new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * 28f);
            if (hasPrev)
            {
                batch.DrawLine(prev, p, clawColor, 3.5f);
                batch.DrawLine(origin + ((p - origin) * 0.72f), p, core, 1.6f);
            }

            prev = p;
            hasPrev = true;
        }
    }
}
