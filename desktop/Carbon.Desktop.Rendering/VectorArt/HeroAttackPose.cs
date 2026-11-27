// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Desktop.Abstractions.World;

namespace Carbon.Desktop.Rendering.VectorArt;

/// <summary>
/// Hero attack clips and held-item wrist curves (<c>docs/art/combat-animation.md</c> §3.2–3.3). Each
/// <see cref="HeroAttackStyle"/> has one <c>base_hero.cvg.json</c> clip whose keys sit on <see cref="HeroAttackTiming"/>
/// boundaries: start, (apex), cock = contact, impact, follow-through = end of active, settle, end. The wrist curve uses
/// the same keys, so the weapon stays locked to the hand through the swing.
/// </summary>
public static class HeroAttackPose
{
    /// <summary>Share of the active window, after contact, at which the impact pose (full extension) is reached.</summary>
    public const float ImpactShareOfActive = 0.35f;

    /// <summary>Share of the anticipation at which an overhead, draw or raise reaches its held apex.</summary>
    public const float ApexShareOfAnticipation = 0.65f;

    /// <summary>Share of the recovery at which the settle key sits.</summary>
    public const float SettleShareOfRecovery = 0.45f;

    // Tip angle relative to the arm's direction, in degrees, at apex, cock, impact, follow-through and settle.
    private static readonly float[] s_slashForehand = [-85f, -85f, -20f, -12f, -60f];
    private static readonly float[] s_slashBackhand = [45f, 45f, 18f, -12f, -60f];
    private static readonly float[] s_thrust = [-110f, -110f, -6f, -2f, -50f];
    private static readonly float[] s_overhead = [-65f, -70f, 22f, 20f, -30f];
    private static readonly float[] s_staff = [-5f, -3f, -18f, -20f, -60f];

    /// <summary>Gets the <c>base_hero.cvg.json</c> clip of <paramref name="style"/>.</summary>
    /// <param name="style">Attack style.</param>
    /// <returns>Clip name, or <see langword="null"/> for <see cref="HeroAttackStyle.None"/>.</returns>
    public static string? ClipName(HeroAttackStyle style) => style switch
    {
        HeroAttackStyle.Jab => "jab",
        HeroAttackStyle.Hook => "hook",
        HeroAttackStyle.SlashForehand => "slash_fore",
        HeroAttackStyle.SlashBackhand => "slash_back",
        HeroAttackStyle.Thrust => "thrust",
        HeroAttackStyle.Overhead => "overhead",
        HeroAttackStyle.BowShot => "bow_shot",
        HeroAttackStyle.StaffCast => "staff_cast",
        HeroAttackStyle.None or _ => null,
    };

    /// <summary>Gets whether <paramref name="style"/> has an apex key held through the end of anticipation.</summary>
    /// <param name="style">Attack style.</param>
    /// <returns><see langword="true"/> for overhead, bow and staff.</returns>
    public static bool HasApex(HeroAttackStyle style) => style is HeroAttackStyle.Overhead or HeroAttackStyle.BowShot or HeroAttackStyle.StaffCast;

    /// <summary>Gets whether the held item follows a wrist curve during <paramref name="style"/> (melee weapons, tools, staff).</summary>
    /// <param name="style">Attack style.</param>
    /// <returns><see langword="true"/> when <see cref="WristDegrees"/> drives the item.</returns>
    public static bool HasWristCurve(HeroAttackStyle style) =>
        style is HeroAttackStyle.SlashForehand or HeroAttackStyle.SlashBackhand or HeroAttackStyle.Thrust or HeroAttackStyle.Overhead or HeroAttackStyle.StaffCast;

    /// <summary>Gets the normalized time of the impact key (full extension), where a confirmed hit presents.</summary>
    /// <param name="timing">Attack timing.</param>
    /// <returns>Normalized clip time.</returns>
    public static float ImpactFraction(in HeroAttackTiming timing) =>
        timing.ContactFraction + (ImpactShareOfActive * (timing.ActiveEndFraction - timing.ContactFraction));

    /// <summary>Gets the normalized time of the settle key.</summary>
    /// <param name="timing">Attack timing.</param>
    /// <returns>Normalized clip time.</returns>
    public static float SettleFraction(in HeroAttackTiming timing) =>
        timing.ActiveEndFraction + (SettleShareOfRecovery * (1f - timing.ActiveEndFraction));

    /// <summary>
    /// Gets the held item's tip angle relative to the arm (degrees) and how much the attack pose owns the item (0 = the
    /// rest pose, 1 = the swing) at <paramref name="progress"/>. Styles without a wrist curve (none, bow) return weight 0.
    /// </summary>
    /// <param name="style">Attack style.</param>
    /// <param name="progress">Normalized clip time.</param>
    /// <param name="weight">Blend weight toward the swing pose.</param>
    /// <returns>Tip angle relative to the arm, in degrees.</returns>
    public static float WristDegrees(HeroAttackStyle style, float progress, out float weight)
    {
        float[]? keys = style switch
        {
            HeroAttackStyle.SlashForehand => s_slashForehand,
            HeroAttackStyle.SlashBackhand => s_slashBackhand,
            HeroAttackStyle.Thrust => s_thrust,
            HeroAttackStyle.Overhead => s_overhead,
            HeroAttackStyle.StaffCast => s_staff,
            HeroAttackStyle.None or HeroAttackStyle.Jab or HeroAttackStyle.Hook or HeroAttackStyle.BowShot or _ => null,
        };
        if (keys is null)
        {
            weight = 0f;
            return 0f;
        }

        HeroAttackTiming timing = HeroAttackTiming.For(style);
        float p = Math.Clamp(progress, 0f, 1f);
        float cock = timing.ContactFraction;
        float impact = ImpactFraction(in timing);
        float follow = timing.ActiveEndFraction;
        float settle = SettleFraction(in timing);

        // The swing owns the item from the cock key to the settle key, blending in and out around them.
        weight = p < cock
            ? KeyframeEasing.Apply(KeyframeEase.InOut, p / MathF.Max(1e-4f, cock))
            : p <= settle ? 1f : 1f - KeyframeEasing.Apply(KeyframeEase.InOut, (p - settle) / MathF.Max(1e-4f, 1f - settle));

        if (p < cock)
        {
            float apex = HasApex(style) ? ApexShareOfAnticipation * cock : 0f;
            return p < apex || apex <= 0f ? keys[0] : Lerp(keys[0], keys[1], KeyframeEasing.Apply(KeyframeEase.InOut, (p - apex) / MathF.Max(1e-4f, cock - apex)));
        }

        if (p < impact)
        {
            return Lerp(keys[1], keys[2], KeyframeEasing.Apply(KeyframeEase.Out, (p - cock) / MathF.Max(1e-4f, impact - cock)));
        }

        if (p < follow)
        {
            return Lerp(keys[2], keys[3], KeyframeEasing.Apply(KeyframeEase.Out, (p - impact) / MathF.Max(1e-4f, follow - impact)));
        }

        return p < settle
            ? Lerp(keys[3], keys[4], KeyframeEasing.Apply(KeyframeEase.InOut, (p - follow) / MathF.Max(1e-4f, settle - follow)))
            : keys[4];
    }

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);
}
