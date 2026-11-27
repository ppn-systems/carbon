// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Networking.Session;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Items.Catalog;
using Carbon.Items.Definitions;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>Hero attack clips, timing and combos (docs/art/combat-animation.md §3).</summary>
public sealed class HeroAttackAnimationTests
{
    private static readonly Lazy<CompiledVectorRig> s_hero = new(() =>
    {
        string artRoot = VectorArtValidator.TryFindArtRootDirectory() ?? throw new InvalidOperationException("art root not found");
        CarbonVectorArtLibrary lib = CarbonVectorArtLibrary.LoadFromDirectory(artRoot);
        return lib.TryGetCharacter(0, out CompiledVectorRig? rig) ? rig : throw new InvalidOperationException("base_hero missing");
    });

    public static TheoryData<HeroAttackStyle> Styles() =>
    [
        HeroAttackStyle.Jab,
        HeroAttackStyle.Hook,
        HeroAttackStyle.SlashForehand,
        HeroAttackStyle.SlashBackhand,
        HeroAttackStyle.Thrust,
        HeroAttackStyle.Overhead,
        HeroAttackStyle.BowShot,
        HeroAttackStyle.StaffCast,
    ];

    [Theory]
    [MemberData(nameof(Styles))]
    public void Timing_ActiveWindow_OverlapsServerDamageTick(HeroAttackStyle style)
    {
        HeroAttackTiming timing = HeroAttackTiming.For(style);
        const float NominalRoundTrip = 0.15f;

        foreach (int tickRate in new[] { 20, 30 })
        {
            // The server damages in the tick that processes the swing (PlayerActionService.Attack): one tick after the
            // input on LAN, later by the round trip online.
            float tick = 1f / tickRate;
            float activeStart = timing.Anticipation;
            float activeEnd = timing.Anticipation + timing.Active;
            activeStart.Should().BeLessThanOrEqualTo(tick + NominalRoundTrip, $"{style} at {tickRate} Hz must make contact before a typical confirmed hit");
            activeEnd.Should().BeGreaterThanOrEqualTo(tick, $"{style} at {tickRate} Hz must still be live when a LAN hit lands");

            // Base swing cooldown, PlayerActionService.GetAttackCooldownTicks: max(10, tickRate / 2) ticks.
            float cooldown = Math.Max(10, tickRate / 2) / (float)tickRate;
            timing.Total.Should().BeLessThan(cooldown, $"{style} must finish before the server allows the next swing at {tickRate} Hz");
        }

        timing.Anticipation.Should().BeLessThanOrEqualTo(HeroAttackTiming.MaxAnticipationSeconds, "a confirmed hit's feedback waits at most this long for contact");
        timing.Active.Should().BePositive();
        timing.Recovery.Should().BeGreaterThan(timing.Active, "recovery is the longest phase (end pose readable)");
    }

    [Theory]
    [MemberData(nameof(Styles))]
    public void HeroClip_KeysSitOnTimingBoundaries(HeroAttackStyle style)
    {
        HeroAttackTiming timing = HeroAttackTiming.For(style);
        string clip = HeroAttackPose.ClipName(style)!;

        s_hero.Value.TryGetAnimation(clip, out CompiledVectorAnimation? anim).Should().BeTrue($"base_hero.cvg.json must define '{clip}'");
        anim!.Loop.Should().BeFalse();
        anim.Duration.Should().BeApproximately(timing.Total, 1e-3f, "the clip lasts the attack's timing");
        anim.PhaseMarks.WindupEnd.Should().BeApproximately(timing.ContactFraction, 1e-3f);
        anim.PhaseMarks.ActiveEnd.Should().BeApproximately(timing.ActiveEndFraction, 1e-3f);

        CompiledKeyframe cock = FindKey(anim, timing.ContactFraction);
        cock.Ease.Should().Be(KeyframeEase.Out, "the strike leaves the cocked pose at full speed");
        _ = FindKey(anim, HeroAttackPose.ImpactFraction(in timing));
        _ = FindKey(anim, timing.ActiveEndFraction);
        anim.Keyframes[^1].Time.Should().Be(1f);
    }

    [Theory]
    [MemberData(nameof(Styles))]
    public void HeroClip_AtImpact_StrikingHandIsInFrontOfShoulder(HeroAttackStyle style)
    {
        HeroAttackTiming timing = HeroAttackTiming.For(style);
        Span<BonePose> bones = stackalloc BonePose[(int)VectorBoneId.Count];

        s_hero.Value.EvaluatePose(HeroAttackPose.ClipName(style)!, "idle", HeroAttackPose.ImpactFraction(in timing), isNormalized: true, bones);

        // A hanging arm rotated by r puts the hand at x = -sin(r) * length (rig space, facing +x): forward means r in (-180°, 0°).
        float handForward = -MathF.Sin(bones[(int)VectorBoneId.ArmFront].RotRad);
        handForward.Should().BePositive($"{style} must strike toward the target, not behind the hero");
        bones[(int)VectorBoneId.Body].RotRad.Should().BeGreaterThan(-0.2f, $"{style} must not lean away at impact");
    }

    [Theory]
    [InlineData(ActionAnimationKind.Punch, new[] { HeroAttackStyle.Jab, HeroAttackStyle.Hook, HeroAttackStyle.Jab, HeroAttackStyle.Hook })]
    [InlineData(ActionAnimationKind.SwordSlash, new[] { HeroAttackStyle.SlashForehand, HeroAttackStyle.SlashBackhand, HeroAttackStyle.Thrust, HeroAttackStyle.SlashForehand })]
    [InlineData(ActionAnimationKind.AxeChop, new[] { HeroAttackStyle.Overhead, HeroAttackStyle.Overhead, HeroAttackStyle.Overhead, HeroAttackStyle.Overhead })]
    public void StyleOf_ConsecutiveSwings_Alternate(ActionAnimationKind kind, HeroAttackStyle[] expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        AttackComboCounter combo = default;
        List<HeroAttackStyle> styles = [];
        for (int i = 0; i < expected.Length; i++)
        {
            styles.Add(HeroAttackTiming.StyleOf(kind, combo.Next(kind, nowSeconds: i * 0.5f)));
        }

        styles.Should().Equal(expected);
    }

    [Fact]
    public void ComboCounter_PauseOrKindChange_RestartsAtFirstStep()
    {
        AttackComboCounter combo = default;

        combo.Next(ActionAnimationKind.SwordSlash, 0f).Should().Be(0);
        combo.Next(ActionAnimationKind.SwordSlash, 0.5f).Should().Be(1);
        combo.Next(ActionAnimationKind.SwordSlash, 0.5f + AttackComboCounter.WindowSeconds + 0.01f).Should().Be(0, "a pause longer than the window restarts the combo");
        combo.Next(ActionAnimationKind.SwordSlash, 2f).Should().Be(1);
        combo.Next(ActionAnimationKind.Punch, 2.2f).Should().Be(0, "a different swing kind restarts the combo");
        combo.Next(ActionAnimationKind.Punch, 1f).Should().Be(0, "a clock that went backwards (reconnect) restarts the combo");
    }

    [Fact]
    public void LeanAt_PullsBackThenCommitsThenSettles()
    {
        HeroAttackTiming timing = HeroAttackTiming.For(HeroAttackStyle.SlashForehand);

        timing.LeanAt(timing.ContactFraction * 0.99f).Should().BeNegative("anticipation pulls the weight back");
        timing.LeanAt(timing.ActiveEndFraction - 1e-3f).Should().BeGreaterThan(2f, "the body is committed toward the target by the end of the strike");
        timing.LeanAt(1f).Should().BeApproximately(0f, 1e-4f, "the lean is gone when the clip ends");
        HeroAttackTiming.For(HeroAttackStyle.None).LeanAt(0.5f).Should().Be(0f);
    }

    [Theory]
    [InlineData(HeroAttackStyle.SlashForehand)]
    [InlineData(HeroAttackStyle.Overhead)]
    [InlineData(HeroAttackStyle.StaffCast)]
    public void WristDegrees_OwnsItemOnlyDuringTheSwing(HeroAttackStyle style)
    {
        HeroAttackTiming timing = HeroAttackTiming.For(style);

        _ = HeroAttackPose.WristDegrees(style, 0f, out float atStart);
        _ = HeroAttackPose.WristDegrees(style, HeroAttackPose.ImpactFraction(in timing), out float atImpact);
        _ = HeroAttackPose.WristDegrees(style, 1f, out float atEnd);

        atStart.Should().Be(0f, "the swing starts from the rest grip without a pop");
        atImpact.Should().Be(1f, "the item is locked to the swing at impact");
        atEnd.Should().Be(0f, "the swing hands the item back to the rest grip");
    }

    [Theory]
    [InlineData(0U, ActionAnimationKind.Punch)]
    [InlineData(StarterGameCatalog.IronSword, ActionAnimationKind.SwordSlash)]
    [InlineData(StarterGameCatalog.WoodAxe, ActionAnimationKind.AxeChop)]
    [InlineData(StarterGameCatalog.StonePickaxe, ActionAnimationKind.PickaxeMine)]
    [InlineData(StarterGameCatalog.HuntingBow, ActionAnimationKind.BowShot)]
    [InlineData(StarterGameCatalog.ArcaneStaff, ActionAnimationKind.StaffCast)]
    [InlineData(StarterGameCatalog.WoodLog, ActionAnimationKind.Punch)]
    public void ResolveActionKind_HeldItem_PicksMotion(uint itemId, ActionAnimationKind expected)
    {
        ItemRules rules = ItemRules.LoadShippedDefaults();
        ItemArchetype? archetype = itemId != 0U && rules.Items.TryGet(itemId, out ItemArchetype? def) ? def : null;

        MultiplayerSession.ResolveActionKind(archetype).Should().Be(expected);
    }

    [Theory]
    [InlineData(ActionAnimationKind.Punch, 1, HeroAttackStyle.Hook)]
    [InlineData(ActionAnimationKind.SwordSlash, 2, HeroAttackStyle.Thrust)]
    [InlineData(ActionAnimationKind.PickaxeMine, 0, HeroAttackStyle.Overhead)]
    public void ResolveActionDuration_HeroAttack_LastsItsTiming(ActionAnimationKind kind, byte combo, HeroAttackStyle style) =>
        MultiplayerSession.ResolveActionDuration(kind, combo).Should().BeApproximately(HeroAttackTiming.For(style).Total, 1e-5f);

    [Fact]
    public void KeyframeEasing_Out_LeavesTheKeyFasterThanSmoothstep()
    {
        KeyframeEasing.Apply(KeyframeEase.Out, 0.25f).Should().BeGreaterThan(KeyframeEasing.Apply(KeyframeEase.InOut, 0.25f) + 0.2f);
        KeyframeEasing.Apply(KeyframeEase.In, 0.25f).Should().BeLessThan(KeyframeEasing.Apply(KeyframeEase.Linear, 0.25f));
        KeyframeEasing.Parse("out").Should().Be(KeyframeEase.Out);
        KeyframeEasing.Parse(null).Should().Be(KeyframeEase.InOut, "keys without an ease keep the original smoothstep");
    }

    private static CompiledKeyframe FindKey(CompiledVectorAnimation anim, float time)
    {
        foreach (CompiledKeyframe key in anim.Keyframes)
        {
            if (MathF.Abs(key.Time - time) < 2e-3f)
            {
                return key;
            }
        }

        throw new Xunit.Sdk.XunitException($"no key at t={time:0.000}");
    }
}
