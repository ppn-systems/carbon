// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Carbon.Graphics.Batching.Common;

namespace Carbon.Desktop.Rendering.VectorArt;

/// <summary>Articulated skeleton bone slots driven by declarative <c>.cvg.json</c> animation tracks.</summary>
public enum VectorBoneId : byte
{
    /// <summary>Root rig origin.</summary>
    Root = 0,

    /// <summary>Cape/cloak (promotes over torso when facing North).</summary>
    Cape = 1,

    /// <summary>Quadruped tail.</summary>
    Tail = 2,

    /// <summary>Left leg/boot.</summary>
    LegLeft = 3,

    /// <summary>Right leg/boot.</summary>
    LegRight = 4,

    /// <summary>Core body / chest armor.</summary>
    Body = 5,

    /// <summary>Off-hand back arm.</summary>
    ArmBack = 6,

    /// <summary>Cranium and eyes.</summary>
    Head = 7,

    /// <summary>Helm, hat, hood, or horns.</summary>
    Headgear = 8,

    /// <summary>Main weapon-bearing front arm.</summary>
    ArmFront = 9,

    /// <summary>Ambient spectral or rune aura.</summary>
    Aura = 10,

    /// <summary>Total bone slot count for fixed stack allocations.</summary>
    Count = 11,
}

/// <summary>Geometric primitive kind inside a vector blueprint.</summary>
public enum VectorShapeKind : byte
{
    /// <summary>Shaded/outlined ellipse.</summary>
    Ellipse = 0,

    /// <summary>Shaded/outlined rectangle.</summary>
    Rect = 1,

    /// <summary>Rounded capsule stroke with end caps.</summary>
    Capsule = 2,

    /// <summary>Closed convex polygon.</summary>
    Polygon = 3,

    /// <summary>Anti-aliased line segment.</summary>
    Line = 4,

    /// <summary>Hollow vector ring.</summary>
    Ring = 5,
}

/// <summary>Directional visibility filter for a vector shape.</summary>
public enum VectorFacingFilter : byte
{
    /// <summary>Visible from all headings.</summary>
    Any = 0,

    /// <summary>Visible only when facing camera (South / SW / SE).</summary>
    South = 1,

    /// <summary>Visible only when facing East/West profile.</summary>
    Side = 2,

    /// <summary>Visible only when facing North / NW / NE.</summary>
    North = 3,

    /// <summary>Visible whenever not facing North.</summary>
    NotNorth = 4,
}

/// <summary>Semantic palette slot role allowing modular skin/hair/eye color substitution without color matching hacks.</summary>
public enum VectorPaletteRole : byte
{
    /// <summary>Static blueprint color.</summary>
    None = 0,

    /// <summary>Primary skin tone surface.</summary>
    Skin = 1,

    /// <summary>Shaded skin tone surface.</summary>
    SkinShadow = 2,

    /// <summary>Hair pigment surface.</summary>
    Hair = 3,

    /// <summary>Eye iris pigment.</summary>
    Eye = 4,
}

/// <summary>Evaluated per-bone runtime transform.</summary>
public struct BonePose
{
    /// <summary>Local X offset.</summary>
    public float Dx { get; set; }

    /// <summary>Local Y offset.</summary>
    public float Dy { get; set; }

    /// <summary>Local rotation in radians.</summary>
    public float RotRad { get; set; }

    /// <summary>Local X scale factor.</summary>
    public float ScaleX { get; set; }

    /// <summary>Local Y scale factor.</summary>
    public float ScaleY { get; set; }

    /// <summary>Returns an identity zero-offset pose.</summary>
    public static BonePose Identity => new() { ScaleX = 1f, ScaleY = 1f };
}

/// <summary>Pre-compiled immutable vector shape ready for zero-allocation batching.</summary>
public readonly struct CompiledVectorShape
{
    /// <summary>Initializes a compiled vector primitive.</summary>
    public CompiledVectorShape(
        VectorShapeKind kind,
        VectorBoneId bone,
        VectorFacingFilter facing,
        Vector2 p0,
        Vector2 p1,
        float rotRad,
        float thickness,
        ColorRgba color,
        bool hasOutline,
        ColorRgba outlineColor,
        float outlineThickness,
        Vector2[]? polygonPoints,
        VectorPaletteRole role = VectorPaletteRole.None)
    {
        this.Kind = kind;
        this.Bone = bone;
        this.Facing = facing;
        this.P0 = p0;
        this.P1 = p1;
        this.RotRad = rotRad;
        this.Thickness = thickness;
        this.Color = color;
        this.HasOutline = hasOutline;
        this.OutlineColor = outlineColor;
        this.OutlineThickness = outlineThickness;
        this.PolygonPoints = polygonPoints;
        this.Role = role;
    }

    /// <summary>Primitive shape type.</summary>
    public VectorShapeKind Kind { get; }

    /// <summary>Bound skeleton bone.</summary>
    public VectorBoneId Bone { get; }

    /// <summary>Facing visibility rule.</summary>
    public VectorFacingFilter Facing { get; }

    /// <summary>Primary origin/center point.</summary>
    public Vector2 P0 { get; }

    /// <summary>Radii, dimensions, or end point.</summary>
    public Vector2 P1 { get; }

    /// <summary>Intrinsic rotation in radians.</summary>
    public float RotRad { get; }

    /// <summary>Line/capsule/ring thickness.</summary>
    public float Thickness { get; }

    /// <summary>Fill or stroke RGBA color.</summary>
    public ColorRgba Color { get; }

    /// <summary>Whether an outline surrounds the shape.</summary>
    public bool HasOutline { get; }

    /// <summary>Outline stroke color.</summary>
    public ColorRgba OutlineColor { get; }

    /// <summary>Outline thickness expansion.</summary>
    public float OutlineThickness { get; }

    /// <summary>Polygon vertex coordinates if <see cref="Kind"/> is <see cref="VectorShapeKind.Polygon"/>.</summary>
    public Vector2[]? PolygonPoints { get; }

    /// <summary>Semantic palette substitution role.</summary>
    public VectorPaletteRole Role { get; }
}

/// <summary>Pre-compiled keyframe pose sample for an animation track.</summary>
public readonly struct CompiledKeyframe
{
    /// <summary>Initializes a keyframe pose snapshot.</summary>
    public CompiledKeyframe(float time, BonePose[] bones)
    {
        this.Time = time;
        this.Bones = bones;
    }

    /// <summary>Normalized <c>[0..1]</c> keyframe timestamp.</summary>
    public float Time { get; }

    /// <summary>Array indexed by <see cref="VectorBoneId"/>.</summary>
    public BonePose[] Bones { get; }
}

/// <summary>Pre-compiled bone animation clip.</summary>
public sealed class CompiledVectorAnimation
{
    /// <summary>Initializes a compiled animation track.</summary>
    public CompiledVectorAnimation(float duration, bool loop, CompiledKeyframe[] keyframes, AttackPhaseMarks? phaseMarks = null)
    {
        this.Duration = MathF.Max(0.05f, duration);
        this.Loop = loop;
        this.Keyframes = keyframes;
        this.PhaseMarks = phaseMarks ?? AttackPhaseMarks.Default;
    }

    /// <summary>Normalized clip times at which each server attack phase ends (attack clips only).</summary>
    public AttackPhaseMarks PhaseMarks { get; }

    /// <summary>Clip duration in seconds.</summary>
    public float Duration { get; }

    /// <summary>Whether the clip loops continuously.</summary>
    public bool Loop { get; }

    /// <summary>Ordered keyframes sorted by normalized timestamp.</summary>
    public CompiledKeyframe[] Keyframes { get; }
}

/// <summary>
/// Pre-compiled, immutable articulated vector rig for a character preset, monster, or NPC loaded from <c>.cvg.json</c>.
/// </summary>
public sealed class CompiledVectorRig
{
    private readonly Vector2[] _bonePivots;
    private readonly Dictionary<string, CompiledVectorAnimation> _animations;

    /// <summary>Initializes a compiled vector rig.</summary>
    public CompiledVectorRig(
        string id,
        string title,
        string subtitle,
        byte level,
        ColorRgba accentColor,
        float designHeight,
        float worldHeight,
        float headTopOffset,
        Vector2 southGrip,
        Vector2 sideGrip,
        Vector2 northGrip,
        Vector2[] bonePivots,
        CompiledVectorShape[] shapes,
        Dictionary<string, CompiledVectorAnimation> animations,
        bool supportsModularHuman = false)
    {
        ArgumentNullException.ThrowIfNull(shapes);
        ArgumentNullException.ThrowIfNull(animations);

        this.Id = id;
        this.Title = title;
        this.Subtitle = subtitle;
        this.Level = level;
        this.AccentColor = accentColor;
        this.DesignHeight = MathF.Max(1f, designHeight);
        this.WorldHeight = MathF.Max(1f, worldHeight);
        this.HeadTopOffset = headTopOffset;
        this.SouthGrip = southGrip;
        this.SideGrip = sideGrip;
        this.NorthGrip = northGrip;
        _bonePivots = bonePivots is not null && bonePivots.Length == (int)VectorBoneId.Count
            ? bonePivots
            : new Vector2[(int)VectorBoneId.Count];
        this.Shapes = shapes;
        _animations = animations;
        this.SupportsModularHuman = supportsModularHuman;
        this.SouthPortraitBounds = ComputePortraitBounds(shapes, VectorFacingFilter.South, this.DesignHeight);
        this.SidePortraitBounds = ComputePortraitBounds(shapes, VectorFacingFilter.Side, this.DesignHeight);
        this.NorthPortraitBounds = ComputePortraitBounds(shapes, VectorFacingFilter.North, this.DesignHeight);
    }

    /// <summary>Pre-computed design-space South facing bounds <c>(minX, minY, maxX, maxY)</c> for O(1) portrait framing.</summary>
    public Vector4 SouthPortraitBounds { get; }

    /// <summary>Pre-computed design-space Side facing bounds <c>(minX, minY, maxX, maxY)</c> for O(1) portrait framing.</summary>
    public Vector4 SidePortraitBounds { get; }

    /// <summary>Pre-computed design-space North facing bounds <c>(minX, minY, maxX, maxY)</c> for O(1) portrait framing.</summary>
    public Vector4 NorthPortraitBounds { get; }

    /// <summary>Whether modular skin/hair/face and armor overlays composite onto this rig.</summary>
    public bool SupportsModularHuman { get; }

    /// <summary>Gets the anatomical joint rotation pivot (in design-space pixels relative to feet) for <paramref name="bone"/>.</summary>
    public Vector2 GetBonePivot(VectorBoneId bone)
    {
        int idx = (int)bone;
        return (uint)idx < (uint)_bonePivots.Length ? _bonePivots[idx] : Vector2.Zero;
    }

    /// <summary>Blueprint identifier.</summary>
    public string Id { get; }

    /// <summary>UI display title.</summary>
    public string Title { get; }

    /// <summary>UI rank/role subtitle.</summary>
    public string Subtitle { get; }

    /// <summary>Display level / tier.</summary>
    public byte Level { get; }

    /// <summary>UI theme accent color.</summary>
    public ColorRgba AccentColor { get; }

    /// <summary>Native rig height in design coordinates.</summary>
    public float DesignHeight { get; }

    /// <summary>Target rendered height in world units.</summary>
    public float WorldHeight { get; }

    /// <summary>World-space vertical clearance above entity center for nameplates &amp; bars.</summary>
    public float HeadTopOffset { get; }

    /// <summary>South weapon hand anchor.</summary>
    public Vector2 SouthGrip { get; }

    /// <summary>East/West profile weapon hand anchor.</summary>
    public Vector2 SideGrip { get; }

    /// <summary>North weapon hand anchor.</summary>
    public Vector2 NorthGrip { get; }

    /// <summary>Ordered vector primitives composing the rig.</summary>
    public CompiledVectorShape[] Shapes { get; }

    /// <summary>Available animation clip names.</summary>
    public IEnumerable<string> ClipNames => _animations.Keys;

    /// <summary>
    /// Looks up a compiled clip by name (case-insensitive).
    /// </summary>
    /// <param name="clipName">Clip name.</param>
    /// <param name="animation">The clip when found.</param>
    /// <returns><see langword="true"/> if the rig has the clip.</returns>
    public bool TryGetAnimation(string clipName, [NotNullWhen(true)] out CompiledVectorAnimation? animation) =>
        _animations.TryGetValue(clipName, out animation);

    /// <summary>
    /// Evaluates interpolated bone transforms into caller-provided <paramref name="outBones"/> (<c>stackalloc</c>) with zero allocations.
    /// </summary>
    public void EvaluatePose(
        string clipName,
        string fallbackClipName,
        float timeOrNormalizedProgress,
        bool isNormalized,
        Span<BonePose> outBones)
    {
        for (int b = 0; b < outBones.Length; b++)
        {
            outBones[b] = BonePose.Identity;
        }

        if (!_animations.TryGetValue(clipName, out CompiledVectorAnimation? anim) &&
            !_animations.TryGetValue(fallbackClipName, out anim))
        {
            return;
        }

        if (anim.Keyframes.Length == 0)
        {
            return;
        }

        float normT;
        if (isNormalized)
        {
            normT = Math.Clamp(timeOrNormalizedProgress, 0f, 1f);
        }
        else if (anim.Loop)
        {
            float m = timeOrNormalizedProgress % anim.Duration;
            if (m < 0f)
            {
                m += anim.Duration;
            }

            normT = m / anim.Duration;
        }
        else
        {
            normT = Math.Clamp(timeOrNormalizedProgress / anim.Duration, 0f, 1f);
        }

        CompiledKeyframe[] kfs = anim.Keyframes;
        if (kfs.Length == 1 || normT <= kfs[0].Time)
        {
            kfs[0].Bones.AsSpan(0, Math.Min(outBones.Length, kfs[0].Bones.Length)).CopyTo(outBones);
            return;
        }

        CompiledKeyframe last = kfs[^1];
        if (normT >= last.Time)
        {
            last.Bones.AsSpan(0, Math.Min(outBones.Length, last.Bones.Length)).CopyTo(outBones);
            return;
        }

        for (int i = 0; i < kfs.Length - 1; i++)
        {
            CompiledKeyframe a = kfs[i];
            CompiledKeyframe b = kfs[i + 1];
            if (normT >= a.Time && normT <= b.Time)
            {
                float span = MathF.Max(0.0001f, b.Time - a.Time);
                float linearAlpha = Math.Clamp((normT - a.Time) / span, 0f, 1f);
                // Smoothstep easing prevents robotic snapping on breathing and limb reversal.
                float alpha = linearAlpha * linearAlpha * (3f - (2f * linearAlpha));
                int count = Math.Min(outBones.Length, (int)VectorBoneId.Count);
                for (int boneIdx = 0; boneIdx < count; boneIdx++)
                {
                    BonePose ba = a.Bones[boneIdx];
                    BonePose bb = b.Bones[boneIdx];
                    outBones[boneIdx] = new BonePose
                    {
                        Dx = ba.Dx + ((bb.Dx - ba.Dx) * alpha),
                        Dy = ba.Dy + ((bb.Dy - ba.Dy) * alpha),
                        RotRad = ba.RotRad + ((bb.RotRad - ba.RotRad) * alpha),
                        ScaleX = ba.ScaleX + ((bb.ScaleX - ba.ScaleX) * alpha),
                        ScaleY = ba.ScaleY + ((bb.ScaleY - ba.ScaleY) * alpha),
                    };
                }

                return;
            }
        }
        return;
    }

    private static Vector4 ComputePortraitBounds(CompiledVectorShape[] shapes, VectorFacingFilter targetFacing, float designHeight)
    {
        bool isSouth = targetFacing == VectorFacingFilter.South;
        bool isSide = targetFacing == VectorFacingFilter.Side;
        bool isNorth = targetFacing == VectorFacingFilter.North;

        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;

        for (int i = 0; i < shapes.Length; i++)
        {
            ref readonly CompiledVectorShape s = ref shapes[i];
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

        if (minX > maxX || minY > maxY)
        {
            return new Vector4(-10f, -designHeight, 10f, 0f);
        }

        return new Vector4(minX, minY, maxX, maxY);
    }
}
