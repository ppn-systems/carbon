// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Desktop.Abstractions.World;

/// <summary>
/// Anticipation, active and recovery spans of a hero attack in seconds (<c>docs/art/combat-animation.md</c> §3.1). The
/// clip lasts their sum and its keys sit on those boundaries (the clip's <c>phaseMarks</c>). The server damages in the
/// tick that processes the swing, so every active window overlaps the first server tick plus a typical round trip, and
/// anticipation stays short enough that a confirmed hit's feedback, held until contact, still feels immediate.
/// </summary>
/// <param name="Anticipation">Wind-up seconds before contact.</param>
/// <param name="Active">Seconds the strike is "live" (contact to end of follow-through).</param>
/// <param name="Recovery">Seconds back to neutral.</param>
public readonly record struct HeroAttackTiming(float Anticipation, float Active, float Recovery)
{
    /// <summary>Longest anticipation allowed: the most a confirmed hit's feedback may wait for contact.</summary>
    public const float MaxAnticipationSeconds = 0.12f;

    /// <summary>Gets the clip duration in seconds.</summary>
    public float Total => this.Anticipation + this.Active + this.Recovery;

    /// <summary>Gets the normalized clip time at which the strike makes contact (start of the active window).</summary>
    public float ContactFraction => this.Total <= 0f ? 0f : this.Anticipation / this.Total;

    /// <summary>Gets the normalized clip time at which the active window ends.</summary>
    public float ActiveEndFraction => this.Total <= 0f ? 0f : (this.Anticipation + this.Active) / this.Total;

    /// <summary>Gets the timing of <paramref name="style"/> (<see langword="default"/> for <see cref="HeroAttackStyle.None"/>).</summary>
    /// <param name="style">Attack style.</param>
    /// <returns>The timing.</returns>
    public static HeroAttackTiming For(HeroAttackStyle style) => style switch
    {
        HeroAttackStyle.Jab => new(0.05f, 0.08f, 0.17f),
        HeroAttackStyle.Hook => new(0.07f, 0.09f, 0.18f),
        HeroAttackStyle.SlashForehand => new(0.07f, 0.10f, 0.19f),
        HeroAttackStyle.SlashBackhand => new(0.07f, 0.10f, 0.19f),
        HeroAttackStyle.Thrust => new(0.06f, 0.08f, 0.22f),
        HeroAttackStyle.Overhead => new(0.11f, 0.09f, 0.22f),
        HeroAttackStyle.BowShot => new(0.10f, 0.06f, 0.20f),
        HeroAttackStyle.StaffCast => new(0.09f, 0.08f, 0.21f),
        HeroAttackStyle.None or _ => default,
    };

    /// <summary>
    /// Gets the attack style for an action kind and combo step: bare hands alternate jab and hook, a one-handed weapon
    /// cycles forehand, backhand, thrust.
    /// </summary>
    /// <param name="kind">Action kind.</param>
    /// <param name="comboStep">Combo step (0-based).</param>
    /// <returns>The style, or <see cref="HeroAttackStyle.None"/> when the kind is not a hero attack.</returns>
    public static HeroAttackStyle StyleOf(ActionAnimationKind kind, byte comboStep) => kind switch
    {
        ActionAnimationKind.Punch => (comboStep % 2) == 0 ? HeroAttackStyle.Jab : HeroAttackStyle.Hook,
        ActionAnimationKind.SwordSlash => (comboStep % 3) switch
        {
            0 => HeroAttackStyle.SlashForehand,
            1 => HeroAttackStyle.SlashBackhand,
            _ => HeroAttackStyle.Thrust,
        },
        ActionAnimationKind.AxeChop or ActionAnimationKind.PickaxeMine or ActionAnimationKind.PlaceStructure => HeroAttackStyle.Overhead,
        ActionAnimationKind.BowShot => HeroAttackStyle.BowShot,
        ActionAnimationKind.StaffCast => HeroAttackStyle.StaffCast,
        ActionAnimationKind.None
            or ActionAnimationKind.MonsterStrike
            or ActionAnimationKind.ConsumeItem
            or ActionAnimationKind.SpinSlash
            or ActionAnimationKind.ArcaneWave
            or ActionAnimationKind.SelfAura
            or ActionAnimationKind.MonsterBowShot
            or ActionAnimationKind.MonsterSpellCast
            or ActionAnimationKind.MonsterHeavySlam
            or _ => HeroAttackStyle.None,
    };

    /// <summary>
    /// Gets the forward commitment (world units toward the aim) at normalized clip time <paramref name="progress"/>: a
    /// slight pull back in anticipation, a peak at contact, then a settle through recovery. Applied on top of the clip
    /// so the lunge reads in all eight facings.
    /// </summary>
    /// <param name="progress">Normalized clip time.</param>
    /// <param name="maxUnits">Peak shift in world units.</param>
    /// <returns>Shift toward the aim.</returns>
    public float LeanAt(float progress, float maxUnits = 2.5f)
    {
        if (this.Total <= 0f)
        {
            return 0f;
        }

        float p = Math.Clamp(progress, 0f, 1f);
        float contact = this.ContactFraction;
        float activeEnd = this.ActiveEndFraction;
        if (p < contact)
        {
            float k = contact <= 0f ? 1f : p / contact;
            return -0.25f * maxUnits * MathF.Sin(k * MathF.PI * 0.5f);
        }

        if (p < activeEnd)
        {
            float k = (p - contact) / MathF.Max(1e-4f, activeEnd - contact);
            float snap = 1f - ((1f - k) * (1f - k) * (1f - k));
            return maxUnits * ((-0.25f * (1f - snap)) + snap);
        }

        float r = (p - activeEnd) / MathF.Max(1e-4f, 1f - activeEnd);
        return maxUnits * (1f - (r * r * (3f - (2f * r))));
    }
}
