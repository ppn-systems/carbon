// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Graphics.Animation.Directions;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;

namespace Carbon.Desktop.Rendering.Effects;

/// <summary>
/// Weapon trail (<c>docs/art/combat-animation.md</c> §3.5): a ribbon along the path the weapon tip actually travels,
/// drawn only through the strike. The path is sampled from the same pose functions that draw the rig and the held
/// item, at fixed steps between the start of the strike and now, so it needs no per-frame history, matches any frame
/// rate and allocates nothing. Bare hands draw a short streak from the knuckles.
/// </summary>
public static class WeaponTrailRenderer
{
    /// <summary>Path samples per trail.</summary>
    public const int Samples = 10;

    /// <summary>Seconds the trail takes to fade after the active window.</summary>
    public const float FadeSeconds = 0.06f;

    /// <summary>Share of the anticipation, before contact, that the trail starts in (the strike leaves the cocked pose then).</summary>
    public const float LeadShareOfAnticipation = 0.15f;

    private const float InnerShare = 0.45f;

    /// <summary>
    /// Gets the normalized clip span the trail covers at <paramref name="progress"/> and its opacity (0 = no trail).
    /// </summary>
    /// <param name="timing">Attack timing.</param>
    /// <param name="progress">Normalized clip time.</param>
    /// <param name="tail">Oldest sampled clip time.</param>
    /// <param name="head">Newest sampled clip time.</param>
    /// <returns>Opacity in <c>0..1</c>.</returns>
    public static float Window(in HeroAttackTiming timing, float progress, out float tail, out float head)
    {
        tail = 0f;
        head = 0f;
        if (timing.Total <= 0f)
        {
            return 0f;
        }

        float start = timing.ContactFraction * (1f - LeadShareOfAnticipation);
        float end = timing.ActiveEndFraction;
        float fade = FadeSeconds / timing.Total;
        if (progress <= start || progress >= end + fade)
        {
            return 0f;
        }

        head = MathF.Min(progress, end);
        if (progress <= end)
        {
            tail = start;
            return 1f;
        }

        // After the strike the tail catches up with the head while the ribbon fades.
        float k = (progress - end) / fade;
        tail = start + ((end - start) * k);
        return 1f - k;
    }

    /// <summary>Draws the trail of <paramref name="action"/> for a hero drawn at <paramref name="footPos"/> (before the attack lean).</summary>
    /// <param name="batch">Geometry batch.</param>
    /// <param name="rig">Hero rig.</param>
    /// <param name="item">Held item art, or <see langword="null"/> for bare hands.</param>
    /// <param name="footPos">Foot position without the attack lean.</param>
    /// <param name="dir8">Facing.</param>
    /// <param name="action">Active attack.</param>
    /// <param name="upgradeLevel">Enhancement level of the held item (+1..+5 gold edge, +6 and up cyan, like the forge sheen).</param>
    public static void Draw(
        GeometryBatch batch,
        CompiledVectorRig rig,
        CompiledItemVectorArt? item,
        Vector2 footPos,
        Direction8 dir8,
        in ActionState action,
        byte upgradeLevel)
    {
        ArgumentNullException.ThrowIfNull(batch);
        Span<Vector2> inner = stackalloc Vector2[Samples + 1];
        Span<Vector2> outer = stackalloc Vector2[Samples + 1];
        float opacity = SamplePath(rig, item, footPos, dir8, in action, inner, outer, out CompiledItemVectorArt? weapon);
        if (opacity <= 0.01f)
        {
            return;
        }

        if (weapon is null)
        {
            DrawStreak(batch, outer, opacity);
            return;
        }

        (ColorRgba body, ColorRgba edge) = TrailColours(weapon, upgradeLevel);
        for (int i = 1; i <= Samples; i++)
        {
            // Older samples fade out; the ribbon is brightest at the blade.
            float k = i / (float)Samples;
            byte a = (byte)(body.A * opacity * k * k);
            ColorRgba fill = new(body.R, body.G, body.B, a);
            batch.FillTriangle(inner[i - 1], outer[i - 1], outer[i], fill);
            batch.FillTriangle(inner[i - 1], outer[i], inner[i], fill);
            batch.DrawLine(outer[i - 1], outer[i], new ColorRgba(edge.R, edge.G, edge.B, (byte)(edge.A * opacity * k)), 0.6f + (0.9f * k));
        }
    }

    /// <summary>
    /// Samples the trail path: <see cref="Samples"/> + 1 points from oldest to newest, where <paramref name="outer"/> is the
    /// weapon tip (the knuckles for bare hands) and <paramref name="inner"/> a point along the blade.
    /// </summary>
    /// <param name="rig">Hero rig.</param>
    /// <param name="item">Held item art, or <see langword="null"/> for bare hands.</param>
    /// <param name="footPos">Foot position without the attack lean.</param>
    /// <param name="dir8">Facing.</param>
    /// <param name="action">Active attack.</param>
    /// <param name="inner">Receives the inner edge (length at least <see cref="Samples"/> + 1).</param>
    /// <param name="outer">Receives the tip path (length at least <see cref="Samples"/> + 1).</param>
    /// <param name="weapon">The item the ribbon follows, or <see langword="null"/> for a fist streak.</param>
    /// <returns>Trail opacity; 0 when there is no trail this frame (and the spans are untouched).</returns>
    public static float SamplePath(
        CompiledVectorRig rig,
        CompiledItemVectorArt? item,
        Vector2 footPos,
        Direction8 dir8,
        in ActionState action,
        Span<Vector2> inner,
        Span<Vector2> outer,
        out CompiledItemVectorArt? weapon)
    {
        ArgumentNullException.ThrowIfNull(rig);
        weapon = null;
        HeroAttackStyle style = action.IsActive ? HeroAttackTiming.StyleOf(action.Kind, action.ComboStep) : HeroAttackStyle.None;
        if (style is HeroAttackStyle.None or HeroAttackStyle.BowShot || HeroAttackPose.ClipName(style) is not string clip)
        {
            return 0f;
        }

        HeroAttackTiming timing = HeroAttackTiming.For(style);
        float opacity = Window(in timing, action.Progress, out float tail, out float head);
        if (opacity <= 0.01f || head <= tail)
        {
            return 0f;
        }

        weapon = style is HeroAttackStyle.Jab or HeroAttackStyle.Hook ? null : item;
        float uniformScale = rig.WorldHeight / rig.DesignHeight;
        bool mirrorX = dir8 is Direction8.West or Direction8.NorthWest or Direction8.SouthWest;
        Span<BonePose> bones = stackalloc BonePose[(int)VectorBoneId.Count];
        for (int i = 0; i <= Samples; i++)
        {
            float t = tail + ((head - tail) * i / Samples);
            ActionState at = action with { RemainingSeconds = action.DurationSeconds * (1f - t) };
            Vector2 foot = footPos + LeanOffset(in at, in timing, t);
            rig.EvaluatePose(clip, "idle", t, isNormalized: true, bones);
            if (weapon is null)
            {
                Vector2 hand = VectorRigRenderer.ComputeHand(rig, foot, dir8, uniformScale, mirrorX, in bones[(int)VectorBoneId.ArmFront]);
                inner[i] = hand;
                outer[i] = hand;
            }
            else
            {
                HeldItemPose held = VectorRigRenderer.ComputeHeldItemPose(rig, weapon, foot, dir8, uniformScale, mirrorX, in bones[(int)VectorBoneId.ArmFront], in at, 0f);
                outer[i] = held.ToWorld(weapon.TipPoint);
                inner[i] = held.ToWorld(Vector2.Lerp(weapon.GripPoint, weapon.TipPoint, InnerShare));
            }
        }

        return opacity;
    }

    /// <summary>Gets the ribbon body and edge colours for a held item: its accent toward white, with the forge edge colour when enhanced.</summary>
    /// <param name="item">Held item art.</param>
    /// <param name="upgradeLevel">Enhancement level.</param>
    /// <returns>Body and edge colours.</returns>
    public static (ColorRgba Body, ColorRgba Edge) TrailColours(CompiledItemVectorArt item, byte upgradeLevel)
    {
        ArgumentNullException.ThrowIfNull(item);
        ColorRgba accent = item.AccentColor;
        float tierAlpha = Math.Clamp(0.55f + (item.Tier * 0.1f), 0.6f, 0.95f);
        ColorRgba body = new(
            (byte)((accent.R + 255) / 2),
            (byte)((accent.G + 255) / 2),
            (byte)((accent.B + 255) / 2),
            (byte)(170 * tierAlpha));
        ColorRgba edge = upgradeLevel >= 6
            ? new ColorRgba(34, 211, 238, 240)
            : upgradeLevel > 0 ? new ColorRgba(250, 204, 21, 235) : new ColorRgba(248, 250, 252, 215);
        return (body, edge);
    }

    private static Vector2 LeanOffset(in ActionState at, in HeroAttackTiming timing, float t)
    {
        Vector2 aim = at.AimDirection;
        return aim.LengthSquared() > 1e-4f ? Vector2.Normalize(aim) * timing.LeanAt(t) : Vector2.Zero;
    }

    private static void DrawStreak(GeometryBatch batch, ReadOnlySpan<Vector2> path, float opacity)
    {
        for (int i = 1; i < path.Length; i++)
        {
            float k = i / (float)(path.Length - 1);
            batch.DrawLine(path[i - 1], path[i], new ColorRgba(248, 250, 252, (byte)(235 * opacity * k)), 0.8f + (2.4f * k));
        }
    }
}
