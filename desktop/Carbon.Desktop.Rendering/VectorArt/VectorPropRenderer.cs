// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Maps.Props;

namespace Carbon.Desktop.Rendering.VectorArt;

/// <summary>
/// Zero-allocation runtime renderer for external <c>props/*.cvg.json</c> blueprints,
/// handling deterministic per-coordinate palette/mirror variation, tree wind sway,
/// hit-flash feedback, progressive harvesting cracks/notches, and nocturnal prop lights.
/// </summary>
internal static class VectorPropRenderer
{
    public static void DrawGround(
        GeometryBatch batch,
        in MapProp prop,
        IVectorArtRepository? art,
        Vector2 shakeOffset = default,
        float hitFlash = 0f)
    {
        if (art is null || !art.TryGetProp((byte)prop.Type, out CompiledPropArt? propArt))
        {
            return;
        }

        uint h = Seed(in prop);
        int variantIdx = propArt.SelectVariantIndex(h, out _);
        CompiledVectorShape[] shapes = propArt.GroundVariants[variantIdx];

        float scale = (prop.Radius > 0.1f ? prop.Radius : propArt.DesignRadius) / propArt.DesignRadius;
        bool mirror = propArt.AllowMirror && ((h >> 5) & 1u) != 0;
        float sx = mirror ? -scale : scale;
        float sy = scale;

        float hpRatio = prop.Destructibility.IsDestructible
            ? Math.Clamp(prop.Destructibility.HealthPercentage, 0f, 1f)
            : 1f;
        float tiltPx = hpRatio < 0.99f ? (1f - hpRatio) * ((h & 1u) == 0 ? 0.10f : -0.10f) : 0f;
        float leanRad = ((AmbientAnimator.Hash01(h, 1) - 0.5f) * 0.08f) + tiltPx;

        BonePose pose = new()
        {
            Dx = 0f,
            Dy = 0f,
            RotRad = leanRad,
            ScaleX = 1f,
            ScaleY = 1f,
        };

        Vector2 origin = prop.WorldPosition + shakeOffset;
        for (int i = 0; i < shapes.Length; i++)
        {
            VectorRigRenderer.DrawShapeAt(batch, in shapes[i], in pose, origin, sx, sy, hitFlash);
        }

        if (hpRatio < 0.99f)
        {
            if (propArt.IsWoody)
            {
                DrawAxeNotch(batch, origin, 10f * scale, leanRad * 26f, hpRatio, h);
            }
            else if (prop.Category == PropCategory.Mineral)
            {
                DrawRockCracks(batch, origin, prop.Radius, hpRatio, h, propArt.IsIcy);
            }
        }
    }

    public static void DrawCanopy(
        GeometryBatch batch,
        in MapProp prop,
        IVectorArtRepository? art,
        byte alpha,
        float swayX = 0f,
        float damageTilt = 0f)
    {
        if (art is null || !art.TryGetProp((byte)prop.Type, out CompiledPropArt? propArt) || !propArt.HasCanopy)
        {
            return;
        }

        uint h = Seed(in prop);
        int variantIdx = propArt.SelectVariantIndex(h, out _);
        CompiledVectorShape[] shapes = propArt.CanopyVariants[variantIdx];
        if (shapes.Length == 0)
        {
            return;
        }

        float scale = (prop.Radius > 0.1f ? prop.Radius : propArt.DesignRadius) / propArt.DesignRadius;
        bool mirror = propArt.AllowMirror && ((h >> 5) & 1u) != 0;
        float sx = mirror ? -scale : scale;
        float sy = scale;
        float leanOffset = (AmbientAnimator.Hash01(h, 1) - 0.5f) * 6f * scale;

        BonePose pose = BonePose.Identity;
        Vector2 origin = prop.WorldPosition + new Vector2(swayX + damageTilt + leanOffset, 0f);
        for (int i = 0; i < shapes.Length; i++)
        {
            VectorRigRenderer.DrawShapeAt(batch, in shapes[i], in pose, origin, sx, sy, hitFlash: 0f, alpha: alpha);
        }
    }

    public static void DrawDestroyedRemnant(
        GeometryBatch batch,
        in MapProp prop,
        IVectorArtRepository? art)
    {
        if (art is null || !art.TryGetProp((byte)prop.Type, out CompiledPropArt? propArt))
        {
            return;
        }

        uint h = Seed(in prop);
        int variantIdx = propArt.SelectVariantIndex(h, out _);
        CompiledVectorShape[] shapes = propArt.RemnantVariants[variantIdx];
        if (shapes.Length == 0)
        {
            return;
        }

        float scale = Math.Clamp((prop.Radius > 0.1f ? prop.Radius : propArt.DesignRadius) / propArt.DesignRadius, 0.75f, 1.35f);
        bool mirror = propArt.AllowMirror && ((h >> 5) & 1u) != 0;
        float sx = mirror ? -scale : scale;

        BonePose pose = BonePose.Identity;
        Vector2 origin = prop.WorldPosition;
        for (int i = 0; i < shapes.Length; i++)
        {
            VectorRigRenderer.DrawShapeAt(batch, in shapes[i], in pose, origin, sx, scale);
        }
    }

    public static bool TryGetLight(
        in MapProp prop,
        IVectorArtRepository? art,
        out PointLight light)
    {
        if (art is not null &&
            art.TryGetProp((byte)prop.Type, out CompiledPropArt? propArt) &&
            propArt.Light is { } lightCfg)
        {
            if (lightCfg.RareOnly)
            {
                uint h = Seed(in prop);
                _ = propArt.SelectVariantIndex(h, out bool isRare);
                if (!isRare)
                {
                    light = default;
                    return false;
                }
            }

            float scale = (prop.Radius > 0.1f ? prop.Radius : propArt.DesignRadius) / propArt.DesignRadius;
            light = new PointLight(
                prop.WorldPosition + (lightCfg.Offset * scale),
                lightCfg.Radius * Math.Clamp(scale, 0.8f, 1.3f),
                lightCfg.Color,
                lightCfg.Intensity);
            return true;
        }

        light = default;
        return false;
    }

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

    private static void DrawAxeNotch(GeometryBatch batch, Vector2 pos, float trunkWidth, float leanOffset, float hpRatio, uint h)
    {
        float severity = Math.Clamp(1f - hpRatio, 0.12f, 1f);
        float sideSign = (h & 1u) == 0 ? 1f : -1f;
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
            float dx = sideSign * (4f + (AmbientAnimator.Hash01(h, 210 + i) * 10f));
            float dy = 13f + (AmbientAnimator.Hash01(h, 220 + i) * 5f);
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
            float angle = (AmbientAnimator.Hash01(h, 300 + i) * MathF.Tau) + (i * 1.1f);
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

        int flecks = 2 + (int)(damage * 4f);
        for (int i = 0; i < flecks; i++)
        {
            float angle = AmbientAnimator.Hash01(h, 340 + i) * MathF.Tau;
            float dist = r * (0.85f + (AmbientAnimator.Hash01(h, 350 + i) * 0.35f));
            Vector2 shard = pos + new Vector2(MathF.Cos(angle) * dist, (MathF.Sin(angle) * dist * 0.65f) + 3f);
            batch.FillCircle(shard, 2.1f, new ColorRgba(100, 116, 139, 220), 5);
        }
    }
}
