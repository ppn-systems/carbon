// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Desktop.Abstractions;
using Carbon.Graphics.Animation.Directions;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;

namespace Carbon.Desktop.Rendering.VectorArt;

/// <summary>
/// Zero-allocation renderer for compiled <see cref="CompiledVectorRig"/> entities and <see cref="CompiledItemVectorArt"/> items.
/// Renders straight to <see cref="GeometryBatch"/> using <c>stackalloc</c> bone &amp; vertex buffers.
/// </summary>
public static class VectorRigRenderer
{
    private const int EllipsePts = 12;
    private static readonly Vector2[] s_unitCircle = BuildUnitCircle(EllipsePts);

    /// <summary>
    /// Draws a declarative articulated vector entity rig at <paramref name="footPosition"/> with directional visibility,
    /// bone keyframe evaluation, hit flash tinting, modular face/hair/skin customization, layered equipped armor,
    /// and dynamically swung held tools or weapons.
    /// </summary>
    public static void DrawEntityRig(
        GeometryBatch batch,
        CompiledVectorRig rig,
        Vector2 footPosition,
        Direction8 direction,
        string clipName,
        float timeOrNormalizedProgress,
        bool isNormalized,
        float targetHeightOverride = 0f,
        float hitFlash = 0f,
        CompiledItemVectorArt? equippedItem = null,
        ClientActionState action = default,
        float swingTimer = 0f,
        byte appearancePreset = 0,
        VisualEquipmentLoadout equipment = default,
        IVectorArtRepository? art = null)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(rig);

        float drawHeight = targetHeightOverride > 0f ? targetHeightOverride : rig.WorldHeight;
        float uniformScale = drawHeight / rig.DesignHeight;
        bool mirrorX = direction is Direction8.West or Direction8.NorthWest or Direction8.SouthWest;
        float sx = mirrorX ? -uniformScale : uniformScale;
        float sy = uniformScale;

        bool isNorth = direction == Direction8.North;
        bool isSide = direction is Direction8.East or Direction8.West or Direction8.SouthEast or Direction8.SouthWest or Direction8.NorthEast or Direction8.NorthWest;
        bool isSouth = !isNorth && !isSide;
        bool isModularHuman = rig.SupportsModularHuman || string.Equals(rig.Id, "base_hero", StringComparison.OrdinalIgnoreCase);

        Span<BonePose> bones = stackalloc BonePose[(int)VectorBoneId.Count];
        rig.EvaluatePose(clipName, "idle", timeOrNormalizedProgress, isNormalized, bones);

        // If North-facing, draw item behind torso first; render non-cape shapes, then promote Cape back shapes on top of torso.
        if (isNorth && equippedItem is not null)
        {
            DrawEquippedItemOnRig(batch, rig, equippedItem, footPosition, direction, uniformScale, mirrorX, in bones[(int)VectorBoneId.ArmFront], action, swingTimer, equipment.WeaponUpgradeLevel);
        }

        if (isModularHuman && !isNorth && equipment.ChestId != 0)
        {
            ModularCharacterOverlays.DrawEquippedBackCape(batch, rig, in bones[(int)VectorBoneId.Cape], footPosition, sx, sy, isNorth: false, equipment.ChestId, art, hitFlash);
        }

        DrawPassShapes(batch, rig, in bones, footPosition, sx, sy, isSouth, isSide, isNorth, capePass: !isNorth, hitFlash, appearancePreset, isModularHuman);
        if (isNorth)
        {
            DrawPassShapes(batch, rig, in bones, footPosition, sx, sy, isSouth, isSide, isNorth, capePass: true, hitFlash, appearancePreset, isModularHuman);
        }

        if (isModularHuman)
        {
            ModularCharacterOverlays.DrawLegAndFootArmor(batch, rig, in bones, footPosition, sx, sy, isNorth, isSide, equipment, art, hitFlash);
            ModularCharacterOverlays.DrawChestAndArmArmor(batch, rig, in bones, footPosition, sx, sy, isNorth, isSide, equipment.ChestId, art, hitFlash);
            if (isNorth && equipment.ChestId != 0)
            {
                ModularCharacterOverlays.DrawEquippedBackCape(batch, rig, in bones[(int)VectorBoneId.Cape], footPosition, sx, sy, isNorth: true, equipment.ChestId, art, hitFlash);
            }

            ModularCharacterOverlays.DrawFaceAndHair(batch, rig, in bones[(int)VectorBoneId.Head], footPosition, sx, sy, isSouth, isSide, isNorth, appearancePreset, equipment.HeadId, hitFlash);
            ModularCharacterOverlays.DrawHeadArmor(batch, rig, in bones[(int)VectorBoneId.Headgear], footPosition, sx, sy, isSouth, isNorth, equipment.HeadId, art, hitFlash);
        }

        if (!isNorth && equippedItem is not null)
        {
            DrawEquippedItemOnRig(batch, rig, equippedItem, footPosition, direction, uniformScale, mirrorX, in bones[(int)VectorBoneId.ArmFront], action, swingTimer, equipment.WeaponUpgradeLevel);
        }
    }

    /// <summary>
    /// Renders an entity rig auto-centered and scaled to fit cleanly inside a square/circular HUD portrait
    /// centered at <paramref name="boxCenter"/> without overflowing borders.
    /// </summary>
    public static void DrawPortraitRig(
        GeometryBatch batch,
        CompiledVectorRig rig,
        Vector2 boxCenter,
        float boxSize,
        Direction8 direction = Direction8.South,
        string clipName = "idle",
        float timeOrNormalizedProgress = 0f,
        CompiledItemVectorArt? equippedItem = null,
        byte appearancePreset = 0,
        VisualEquipmentLoadout equipment = default,
        IVectorArtRepository? art = null)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(rig);

        bool isNorth = direction == Direction8.North;
        bool isSide = direction is Direction8.East or Direction8.West or Direction8.SouthEast or Direction8.SouthWest or Direction8.NorthEast or Direction8.NorthWest;
        bool isSouth = !isNorth && !isSide;

        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minY = float.MaxValue;
        float maxY = float.MinValue;

        for (int i = 0; i < rig.Shapes.Length; i++)
        {
            ref readonly CompiledVectorShape s = ref rig.Shapes[i];
            if (s.Bone == VectorBoneId.Root && s.P0.Y >= -3f)
            {
                continue;
            }

            bool visible = s.Facing switch
            {
                VectorFacingFilter.South => isSouth,
                VectorFacingFilter.Side => isSide,
                VectorFacingFilter.North => isNorth,
                VectorFacingFilter.NotNorth => !isNorth,
                VectorFacingFilter.Any or _ => true,
            };
            if (!visible)
            {
                continue;
            }

            ExpandBoundsForShape(in s, ref minX, ref maxX, ref minY, ref maxY);
        }

        if (minX > maxX || minY > maxY)
        {
            minX = -10f;
            maxX = 10f;
            minY = -rig.DesignHeight;
            maxY = 0f;
        }

        float spanW = MathF.Max(10f, maxX - minX);
        float spanH = MathF.Max(10f, maxY - minY);
        float fitScale = boxSize * 0.78f / MathF.Max(spanW, spanH);
        float targetHeight = fitScale * rig.DesignHeight;
        float centerDesignX = (minX + maxX) * 0.5f;
        float centerDesignY = (minY + maxY) * 0.5f;
        Vector2 footPos = new(
            boxCenter.X - (centerDesignX * fitScale),
            boxCenter.Y - (centerDesignY * fitScale));

        DrawEntityRig(
            batch,
            rig,
            footPos,
            direction,
            clipName,
            timeOrNormalizedProgress,
            isNormalized: false,
            targetHeightOverride: targetHeight,
            hitFlash: 0f,
            equippedItem: equippedItem,
            appearancePreset: appearancePreset,
            equipment: equipment,
            art: art);
    }

    private static void ExpandBoundsForShape(
        in CompiledVectorShape s,
        ref float minX,
        ref float maxX,
        ref float minY,
        ref float maxY)
    {
        switch (s.Kind)
        {
            case VectorShapeKind.Ellipse:
                minX = MathF.Min(minX, s.P0.X - s.P1.X);
                maxX = MathF.Max(maxX, s.P0.X + s.P1.X);
                minY = MathF.Min(minY, s.P0.Y - s.P1.Y);
                maxY = MathF.Max(maxY, s.P0.Y + s.P1.Y);
                break;

            case VectorShapeKind.Rect:
                minX = MathF.Min(minX, s.P0.X);
                maxX = MathF.Max(maxX, s.P0.X + s.P1.X);
                minY = MathF.Min(minY, s.P0.Y);
                maxY = MathF.Max(maxY, s.P0.Y + s.P1.Y);
                break;

            case VectorShapeKind.Capsule:
            case VectorShapeKind.Line:
                float half = s.Thickness * 0.5f;
                minX = MathF.Min(minX, MathF.Min(s.P0.X, s.P1.X) - half);
                maxX = MathF.Max(maxX, MathF.Max(s.P0.X, s.P1.X) + half);
                minY = MathF.Min(minY, MathF.Min(s.P0.Y, s.P1.Y) - half);
                maxY = MathF.Max(maxY, MathF.Max(s.P0.Y, s.P1.Y) + half);
                break;

            case VectorShapeKind.Polygon:
                if (s.PolygonPoints is not null)
                {
                    for (int v = 0; v < s.PolygonPoints.Length; v++)
                    {
                        minX = MathF.Min(minX, s.PolygonPoints[v].X);
                        maxX = MathF.Max(maxX, s.PolygonPoints[v].X);
                        minY = MathF.Min(minY, s.PolygonPoints[v].Y);
                        maxY = MathF.Max(maxY, s.PolygonPoints[v].Y);
                    }
                }
                break;

            case VectorShapeKind.Ring:
                minX = MathF.Min(minX, s.P0.X - s.P1.X);
                maxX = MathF.Max(maxX, s.P0.X + s.P1.X);
                minY = MathF.Min(minY, s.P0.Y - s.P1.X);
                maxY = MathF.Max(maxY, s.P0.Y + s.P1.X);
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Draws an inventory or UI item vector icon centered at <paramref name="center"/> scaled cleanly to <paramref name="targetSize"/>.
    /// </summary>
    public static void DrawItemIcon(
        GeometryBatch batch,
        CompiledItemVectorArt item,
        Vector2 center,
        float targetSize,
        float rotationRad = 0f)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(item);

        // Item shapes span roughly [-10..10] -> 20 design units diameter.
        float scale = targetSize / 20f;
        BonePose rootPose = new() { RotRad = rotationRad, ScaleX = 1f, ScaleY = 1f };

        for (int i = 0; i < item.Shapes.Length; i++)
        {
            DrawSingleShape(batch, in item.Shapes[i], Vector2.Zero, in rootPose, center, scale, scale, hitFlash: 0f);
        }
    }

    private static void DrawEquippedItemOnRig(
        GeometryBatch batch,
        CompiledVectorRig rig,
        CompiledItemVectorArt item,
        Vector2 footPos,
        Direction8 dir8,
        float uniformScale,
        bool mirrorX,
        in BonePose frontArmPose,
        in ClientActionState action,
        float swingTimer,
        byte weaponUpgradeLevel = 0)
    {
        if (!item.IsHeldVisible && !action.IsActive)
        {
            return;
        }

        Vector2 baseGrip = dir8 == Direction8.North
            ? rig.NorthGrip
            : (dir8 == Direction8.South ? rig.SouthGrip : rig.SideGrip);
        Vector2 armPivot = rig.GetBonePivot(VectorBoneId.ArmFront);
        Vector2 gripRel = new(
            (baseGrip.X - armPivot.X) * frontArmPose.ScaleX,
            (baseGrip.Y - armPivot.Y) * frontArmPose.ScaleY);
        Vector2 rotatedRel = Rotate(gripRel, frontArmPose.RotRad);
        float handLocalX = armPivot.X + rotatedRel.X + frontArmPose.Dx;
        float handLocalY = armPivot.Y + rotatedRel.Y + frontArmPose.Dy;
        Vector2 handWorld = new(
            footPos.X + (handLocalX * (mirrorX ? -uniformScale : uniformScale)),
            footPos.Y + (handLocalY * uniformScale));

        Vector2 dirVec = action.IsActive && action.AimDirection.LengthSquared() > 1e-4f
            ? action.AimDirection
            : dir8.ToVector();
        float aimRad = MathF.Atan2(dirVec.Y, dirVec.X);
        float baseItemRot = aimRad + (MathF.PI * 0.25f);
        float armFollowRot = mirrorX ? -frontArmPose.RotRad : frontArmPose.RotRad;
        float swingOffset;
        float thrustFactor = 0f;

        if (action.IsActive)
        {
            float p = action.Progress;
            switch (action.Kind)
            {
                case ClientActionKind.AxeChop:
                    swingOffset = EvaluateSwingArc(p, -1.62f, 1.05f, out thrustFactor);
                    break;
                case ClientActionKind.PickaxeMine:
                    swingOffset = EvaluateSwingArc(p, -1.85f, 0.88f, out thrustFactor);
                    break;
                case ClientActionKind.SwordSlash:
                case ClientActionKind.SpinSlash:
                    swingOffset = (action.ComboStep & 1) == 0
                        ? EvaluateSwingArc(p, -1.35f, 1.28f, out thrustFactor)
                        : EvaluateSwingArc(p, 1.35f, -1.28f, out thrustFactor);
                    break;
                case ClientActionKind.PlaceStructure:
                    swingOffset = -0.6f + (p * 0.8f);
                    break;
                case ClientActionKind.BowShot:
                    swingOffset = -0.35f + (MathF.Sin(p * MathF.PI) * 0.20f);
                    thrustFactor = p < 0.55f ? p / 0.55f * 0.35f : 0f;
                    break;
                case ClientActionKind.StaffCast:
                    swingOffset = -0.92f + (MathF.Sin(p * MathF.PI) * 0.68f);
                    thrustFactor = MathF.Sin(p * MathF.PI) * 0.55f;
                    break;
                case ClientActionKind.ConsumeItem:
                case ClientActionKind.MonsterStrike:
                case ClientActionKind.ArcaneWave:
                case ClientActionKind.SelfAura:
                case ClientActionKind.None:
                default:
                    swingOffset = 0f;
                    break;
            }
        }
        else if (swingTimer > 0f)
        {
            float p = 1f - Math.Clamp(swingTimer / 0.24f, 0f, 1f);
            swingOffset = EvaluateSwingArc(p, -1.30f, 1.20f, out thrustFactor);
        }
        else
        {
            swingOffset = item.RestAngleRad + armFollowRot;
        }

        handWorld += dirVec * (thrustFactor * 4.5f * uniformScale);

        float totalRot = baseItemRot + swingOffset;
        float itemScale = uniformScale * item.WorldScale * 0.9f;
        Vector2 rotatedGrip = Rotate(item.GripPoint * itemScale, totalRot);
        Vector2 itemCenter = handWorld - rotatedGrip;
        BonePose pose = new() { RotRad = totalRot, ScaleX = 1f, ScaleY = 1f };

        if (weaponUpgradeLevel > 0)
        {
            ColorRgba sheen = weaponUpgradeLevel >= 6
                ? new ColorRgba(34, 211, 238, 125)
                : new ColorRgba(250, 204, 21, 115);
            Vector2 auraTip = itemCenter + Rotate(new Vector2(3.5f * itemScale, -3.5f * itemScale), totalRot);
            batch.FillCircle(auraTip, (5.5f + (weaponUpgradeLevel * 0.45f)) * itemScale, sheen, 12);
        }

        for (int i = 0; i < item.Shapes.Length; i++)
        {
            DrawSingleShape(batch, in item.Shapes[i], Vector2.Zero, in pose, itemCenter, itemScale, itemScale, hitFlash: 0f);
        }

        if (action.IsActive && action.Kind == ClientActionKind.BowShot && action.Progress < 0.68f)
        {
            float drawFrac = MathF.Sin(Math.Clamp(action.Progress / 0.65f, 0f, 1f) * MathF.PI);
            float pullDist = 4.6f * drawFrac;
            Vector2 topLimb = itemCenter + Rotate(new Vector2(-3.2f, -8.2f) * itemScale, totalRot);
            Vector2 botLimb = itemCenter + Rotate(new Vector2(-3.2f, 8.2f) * itemScale, totalRot);
            Vector2 nockPt = itemCenter + Rotate(new Vector2(-3.2f - pullDist, 0f) * itemScale, totalRot);
            Vector2 arrowTip = itemCenter + Rotate(new Vector2(9.2f - (pullDist * 0.35f), 0f) * itemScale, totalRot);
            batch.DrawLine(topLimb, nockPt, new ColorRgba(248, 250, 252, 235), 1.3f * itemScale);
            batch.DrawLine(botLimb, nockPt, new ColorRgba(248, 250, 252, 235), 1.3f * itemScale);
            batch.DrawLine(nockPt, arrowTip, new ColorRgba(253, 224, 71, 240), 1.6f * itemScale);
            batch.FillCircle(arrowTip, 1.8f * itemScale, item.AccentColor, 8);
        }
        else if (action.IsActive && action.Kind == ClientActionKind.StaffCast)
        {
            float pulse = MathF.Sin(action.Progress * MathF.PI);
            Vector2 focusPos = itemCenter + Rotate(new Vector2(6.2f, -6.2f) * itemScale, totalRot);
            float ringRadius = (4.8f + (pulse * 5.2f)) * itemScale;
            byte ringAlpha = (byte)Math.Clamp((int)(pulse * 220f), 40, 220);
            batch.DrawCircle(focusPos, ringRadius, new ColorRgba(item.AccentColor.R, item.AccentColor.G, item.AccentColor.B, ringAlpha), 16, 1.4f * itemScale);
            batch.FillCircle(focusPos, (2.4f + (pulse * 1.8f)) * itemScale, new ColorRgba(224, 242, 254, ringAlpha), 10);
        }
    }

    private static float EvaluateSwingArc(float progress, float windupRad, float strikeRad, out float thrustFactor)
    {
        float p = Math.Clamp(progress, 0f, 1f);
        if (p < 0.24f)
        {
            float k = p / 0.24f;
            float easeOut = 1f - ((1f - k) * (1f - k));
            thrustFactor = -0.22f * easeOut;
            return windupRad * (0.45f + (0.55f * easeOut));
        }

        if (p < 0.56f)
        {
            float t = (p - 0.24f) / 0.32f;
            float snap = 1f - MathF.Pow(1f - t, 3f);
            thrustFactor = MathF.Sin(t * MathF.PI);
            return windupRad + ((strikeRad - windupRad) * snap);
        }

        float rec = (p - 0.56f) / 0.44f;
        float decay = 1f - ((1f - rec) * (1f - rec));
        thrustFactor = (1f - decay) * 0.25f;
        return strikeRad * (1f - (0.35f * decay));
    }

    private static void DrawPassShapes(
        GeometryBatch batch,
        CompiledVectorRig rig,
        in Span<BonePose> bones,
        Vector2 origin,
        float sx,
        float sy,
        bool isSouth,
        bool isSide,
        bool isNorth,
        bool capePass,
        float hitFlash,
        byte appearancePreset = 0,
        bool isBaseHero = false)
    {
        CompiledVectorShape[] shapes = rig.Shapes;
        for (int i = 0; i < shapes.Length; i++)
        {
            ref readonly CompiledVectorShape shape = ref shapes[i];
            if (isNorth)
            {
                bool isCapeShape = shape.Bone == VectorBoneId.Cape;
                if (isCapeShape != capePass)
                {
                    continue;
                }
            }

            bool visible = shape.Facing switch
            {
                VectorFacingFilter.South => isSouth,
                VectorFacingFilter.Side => isSide,
                VectorFacingFilter.North => isNorth,
                VectorFacingFilter.NotNorth => !isNorth,
                VectorFacingFilter.Any or _ => true,
            };
            if (!visible)
            {
                continue;
            }

            BonePose bonePose = bones[(int)shape.Bone];
            Vector2 bonePivot = rig.GetBonePivot(shape.Bone);
            DrawSingleShape(batch, in shape, bonePivot, in bonePose, origin, sx, sy, hitFlash, appearancePreset, isBaseHero);
        }
    }

    private static void DrawSingleShape(
        GeometryBatch batch,
        in CompiledVectorShape s,
        Vector2 bonePivot,
        in BonePose bone,
        Vector2 origin,
        float sx,
        float sy,
        float hitFlash,
        byte appearancePreset = 0,
        bool isBaseHero = false)
    {
        ColorRgba baseCol = ModularCharacterOverlays.ResolveModularColor(in s, appearancePreset, isBaseHero);
        ColorRgba fill = ApplyHitFlash(baseCol, hitFlash);
        ColorRgba outline = s.HasOutline ? ApplyHitFlash(s.OutlineColor, hitFlash * 0.45f) : default;
        float cosBone = MathF.Cos(bone.RotRad);
        float sinBone = MathF.Sin(bone.RotRad);

        switch (s.Kind)
        {
            case VectorShapeKind.Ellipse:
                DrawEllipse(batch, in s, bonePivot, in bone, origin, sx, sy, cosBone, sinBone, fill, s.HasOutline, outline);
                break;

            case VectorShapeKind.Rect:
                DrawRect(batch, in s, bonePivot, in bone, origin, sx, sy, cosBone, sinBone, fill, s.HasOutline, outline);
                break;

            case VectorShapeKind.Polygon:
                DrawPolygon(batch, in s, bonePivot, in bone, origin, sx, sy, cosBone, sinBone, fill, s.HasOutline, outline);
                break;

            case VectorShapeKind.Capsule:
                {
                    Vector2 a = Transform(s.P0, bonePivot, in bone, origin, sx, sy, cosBone, sinBone);
                    Vector2 b = Transform(s.P1, bonePivot, in bone, origin, sx, sy, cosBone, sinBone);
                    float w = s.Thickness * MathF.Abs(sx);
                    if (s.HasOutline)
                    {
                        float ow = w + (s.OutlineThickness * MathF.Abs(sx) * 1.4f);
                        batch.DrawLine(a, b, outline, ow);
                        batch.FillCircle(a, ow * 0.5f, outline, 10);
                        batch.FillCircle(b, ow * 0.5f, outline, 10);
                    }

                    batch.DrawLine(a, b, fill, w);
                    batch.FillCircle(a, w * 0.5f, fill, 10);
                    batch.FillCircle(b, w * 0.5f, fill, 10);
                    break;
                }

            case VectorShapeKind.Line:
                {
                    Vector2 a = Transform(s.P0, bonePivot, in bone, origin, sx, sy, cosBone, sinBone);
                    Vector2 b = Transform(s.P1, bonePivot, in bone, origin, sx, sy, cosBone, sinBone);
                    batch.DrawLine(a, b, fill, MathF.Max(1f, s.Thickness * MathF.Abs(sx)));
                    break;
                }

            case VectorShapeKind.Ring:
                {
                    Vector2 c = Transform(s.P0, bonePivot, in bone, origin, sx, sy, cosBone, sinBone);
                    float r = s.P1.X * MathF.Abs(sx);
                    batch.DrawCircle(c, r, fill, 16, MathF.Max(1f, s.Thickness * MathF.Abs(sx)));
                    break;
                }

            default:
                break;
        }
    }

    private static void DrawEllipse(
        GeometryBatch batch,
        in CompiledVectorShape s,
        Vector2 bonePivot,
        in BonePose bone,
        Vector2 origin,
        float sx,
        float sy,
        float cosB,
        float sinB,
        ColorRgba fill,
        bool hasOutline,
        ColorRgba outline)
    {
        Span<Vector2> pts = stackalloc Vector2[EllipsePts];
        float cosL = MathF.Cos(s.RotRad);
        float sinL = MathF.Sin(s.RotRad);
        bool invertOrder = (sx * sy) < 0f;

        if (hasOutline)
        {
            FillEllipseSpan(pts, s.P0, s.P1.X + s.OutlineThickness, s.P1.Y + s.OutlineThickness, cosL, sinL, invertOrder, bonePivot, in bone, origin, sx, sy, cosB, sinB);
            batch.FillConvexPolygon(pts, outline);
        }

        FillEllipseSpan(pts, s.P0, s.P1.X, s.P1.Y, cosL, sinL, invertOrder, bonePivot, in bone, origin, sx, sy, cosB, sinB);
        batch.FillConvexPolygon(pts, fill);
    }

    private static void FillEllipseSpan(
        Span<Vector2> pts,
        Vector2 center,
        float rx,
        float ry,
        float cosL,
        float sinL,
        bool invertOrder,
        Vector2 bonePivot,
        in BonePose bone,
        Vector2 origin,
        float sx,
        float sy,
        float cosB,
        float sinB)
    {
        for (int i = 0; i < EllipsePts; i++)
        {
            Vector2 u = s_unitCircle[invertOrder ? (EllipsePts - 1 - i) : i];
            float lx = u.X * rx;
            float ly = u.Y * ry;
            Vector2 local = new(center.X + (lx * cosL) - (ly * sinL), center.Y + (lx * sinL) + (ly * cosL));
            pts[i] = Transform(local, bonePivot, in bone, origin, sx, sy, cosB, sinB);
        }
    }

    private static void DrawRect(
        GeometryBatch batch,
        in CompiledVectorShape s,
        Vector2 bonePivot,
        in BonePose bone,
        Vector2 origin,
        float sx,
        float sy,
        float cosB,
        float sinB,
        ColorRgba fill,
        bool hasOutline,
        ColorRgba outline)
    {
        Span<Vector2> quad = stackalloc Vector2[4];
        Vector2 c = new(s.P0.X + (s.P1.X * 0.5f), s.P0.Y + (s.P1.Y * 0.5f));
        float hw = s.P1.X * 0.5f;
        float hh = s.P1.Y * 0.5f;
        float cosL = MathF.Cos(s.RotRad);
        float sinL = MathF.Sin(s.RotRad);

        if (hasOutline)
        {
            float pad = s.OutlineThickness;
            FillQuadSpan(quad, c, hw + pad, hh + pad, cosL, sinL, bonePivot, in bone, origin, sx, sy, cosB, sinB);
            batch.FillConvexPolygon(quad, outline);
        }

        FillQuadSpan(quad, c, hw, hh, cosL, sinL, bonePivot, in bone, origin, sx, sy, cosB, sinB);
        batch.FillConvexPolygon(quad, fill);
    }

    private static void FillQuadSpan(
        Span<Vector2> quad,
        Vector2 c,
        float hw,
        float hh,
        float cosL,
        float sinL,
        Vector2 bonePivot,
        in BonePose bone,
        Vector2 origin,
        float sx,
        float sy,
        float cosB,
        float sinB)
    {
        quad[0] = Transform(RotateOffset(c, -hw, -hh, cosL, sinL), bonePivot, in bone, origin, sx, sy, cosB, sinB);
        quad[1] = Transform(RotateOffset(c, hw, -hh, cosL, sinL), bonePivot, in bone, origin, sx, sy, cosB, sinB);
        quad[2] = Transform(RotateOffset(c, hw, hh, cosL, sinL), bonePivot, in bone, origin, sx, sy, cosB, sinB);
        quad[3] = Transform(RotateOffset(c, -hw, hh, cosL, sinL), bonePivot, in bone, origin, sx, sy, cosB, sinB);
    }

    private static void DrawPolygon(
        GeometryBatch batch,
        in CompiledVectorShape s,
        Vector2 bonePivot,
        in BonePose bone,
        Vector2 origin,
        float sx,
        float sy,
        float cosB,
        float sinB,
        ColorRgba fill,
        bool hasOutline,
        ColorRgba outline)
    {
        Vector2[]? rawPts = s.PolygonPoints;
        if (rawPts is null || rawPts.Length < 3)
        {
            return;
        }

        Span<Vector2> pts = stackalloc Vector2[rawPts.Length];
        for (int i = 0; i < rawPts.Length; i++)
        {
            pts[i] = Transform(rawPts[i], bonePivot, in bone, origin, sx, sy, cosB, sinB);
        }

        batch.FillConvexPolygon(pts, fill);
        if (hasOutline)
        {
            float stroke = MathF.Max(1f, s.OutlineThickness * MathF.Abs(sx));
            for (int i = 0; i < pts.Length; i++)
            {
                batch.DrawLine(pts[i], pts[(i + 1) % pts.Length], outline, stroke);
            }
        }
    }

    private static Vector2 Transform(
        Vector2 local,
        Vector2 bonePivot,
        in BonePose bone,
        Vector2 origin,
        float sx,
        float sy,
        float cosB,
        float sinB)
    {
        float lx = (local.X - bonePivot.X) * bone.ScaleX;
        float ly = (local.Y - bonePivot.Y) * bone.ScaleY;
        float rx = bonePivot.X + (lx * cosB) - (ly * sinB) + bone.Dx;
        float ry = bonePivot.Y + (lx * sinB) + (ly * cosB) + bone.Dy;
        return new Vector2(origin.X + (rx * sx), origin.Y + (ry * sy));
    }

    private static Vector2 RotateOffset(Vector2 c, float dx, float dy, float cos, float sin) =>
        new(c.X + (dx * cos) - (dy * sin), c.Y + (dx * sin) + (dy * cos));

    private static Vector2 Rotate(Vector2 v, float rad)
    {
        float c = MathF.Cos(rad);
        float s = MathF.Sin(rad);
        return new Vector2((v.X * c) - (v.Y * s), (v.X * s) + (v.Y * c));
    }

    private static ColorRgba ApplyHitFlash(ColorRgba src, float flash)
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

    private static Vector2[] BuildUnitCircle(int count)
    {
        Vector2[] arr = new Vector2[count];
        float step = MathF.Tau / count;
        for (int i = 0; i < count; i++)
        {
            float a = i * step;
            arr[i] = new Vector2(MathF.Cos(a), MathF.Sin(a));
        }

        return arr;
    }
}
