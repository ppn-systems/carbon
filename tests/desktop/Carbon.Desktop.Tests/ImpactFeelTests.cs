// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Numerics;
using Carbon.Desktop.Abstractions.Events;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Networking.Session;
using Carbon.Desktop.Rendering.Feedback;
using Carbon.Desktop.Rendering.Hud;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Desktop.Rendering.World;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>Hit-stop, screen shake, target flash and their settings (docs/art/combat-animation.md §3.4).</summary>
public sealed class ImpactFeelTests
{
    private const ulong Hero = 7UL;
    private const ulong Slime = 42UL;
    private const float Dt = 1f / 60f;

    private static readonly HeroAttackTiming s_slash = HeroAttackTiming.For(HeroAttackStyle.SlashForehand);

    private static ActionState Slash(float progress) =>
        new(ActionAnimationKind.SwordSlash, s_slash.Total, s_slash.Total * (1f - progress), Vector2.UnitX, 3U, 0);

    private static StrikeImpactEvent HeroHitsSlime(float damage = 10f, bool crit = false) =>
        new(Hero, Slime, new Vector2(30f, 0f), damage, 50f, crit, IsKilled: false, IsLocalAttacker: true, IsLocalVictim: false);

    [Fact]
    public void Remap_HoldsAtImpactThenCatchesUpByTheEnd()
    {
        const float At = 0.3f;
        const float Length = 0.15f;

        HitStop.Remap(0.2f, At, Length).Should().Be(0.2f, "before the impact the swing plays normally");
        HitStop.Remap(0.35f, At, Length).Should().Be(At, "the pose holds during the freeze");
        HitStop.Remap(At + Length, At, Length).Should().BeApproximately(At, 1e-5f, "the catch-up starts where the freeze ended");
        HitStop.Remap(1f, At, Length).Should().BeApproximately(1f, 1e-5f, "the clip still ends when the action ends");

        float previous = 0f;
        for (int i = 0; i <= 100; i++)
        {
            float shown = HitStop.Remap(i / 100f, At, Length);
            shown.Should().BeGreaterThanOrEqualTo(previous - 1e-6f, "displayed time never runs backwards");
            previous = shown;
        }
    }

    [Fact]
    public void Step_HitArrivesDuringWindup_PresentsAtContact()
    {
        ImpactPresenter presenter = new();
        float progress = 0.05f;
        presenter.Step(Slash(progress), Vector2.UnitX, [HeroHitsSlime()], Dt, ImpactFeelOptions.Default);

        presenter.FlashFor(Slime).Should().Be(0f, "on LAN the hit arrives before the blade does; the target must not react yet");
        presenter.IsFrozen.Should().BeFalse();

        float impact = HeroAttackPose.ImpactFraction(in s_slash);
        bool presented = false;
        while (progress < 1f && !presented)
        {
            progress += Dt / s_slash.Total;
            presenter.Step(Slash(progress), Vector2.UnitX, [], Dt, ImpactFeelOptions.Default);
            presented = presenter.FlashFor(Slime) > 0f;
        }

        presented.Should().BeTrue();
        progress.Should().BeApproximately(impact, Dt / s_slash.Total * 1.5f, "the target reacts when the swing reaches its impact pose");
        presenter.IsFrozen.Should().BeTrue("the hit-stop starts with the impact");
        presenter.Trauma.Should().BePositive();
    }

    [Fact]
    public void FlashFor_HoldsAtFullThroughTheHitStop()
    {
        ImpactPresenter presenter = new();
        float progress = 0.6f;
        presenter.Step(Slash(progress), Vector2.UnitX, [HeroHitsSlime(damage: 50f)], Dt, ImpactFeelOptions.Default);

        int frozenFrames = 0;
        while (presenter.IsFrozen)
        {
            presenter.FlashFor(Slime).Should().Be(1f, "the target stays white while the blow is held");
            progress += Dt / s_slash.Total;
            presenter.Step(Slash(progress), Vector2.UnitX, [], Dt, ImpactFeelOptions.Default);
            frozenFrames++;
        }

        frozenFrames.Should().BeGreaterThan(2);
        for (int i = 0; i < 12; i++)
        {
            presenter.Step(Slash(MathF.Min(0.99f, progress + (i * 0.01f))), Vector2.UnitX, [], Dt, ImpactFeelOptions.Default);
        }

        presenter.FlashFor(Slime).Should().BeLessThan(1f, "after the stop the flash fades");
    }

    [Fact]
    public void Step_HitArrivesAfterContact_PresentsAtOnce()
    {
        ImpactPresenter presenter = new();

        presenter.Step(Slash(0.6f), Vector2.UnitX, [HeroHitsSlime()], Dt, ImpactFeelOptions.Default);

        presenter.FlashFor(Slime).Should().BePositive("a late confirmation (high ping) shows as soon as it arrives");
    }

    [Fact]
    public void HitStop_NeverDelaysTheSessionsSwing()
    {
        ImpactPresenter presenter = new();
        float impact = HeroAttackPose.ImpactFraction(in s_slash);
        float progress = impact - 0.01f;
        presenter.Step(Slash(progress), Vector2.UnitX, [HeroHitsSlime(damage: 50f, crit: true)], Dt, ImpactFeelOptions.Default);

        float frozenShown = -1f;
        int frames = 0;
        while (progress < 1f)
        {
            // The session's clock (input, cooldown, simulation) advances every frame whatever the presentation shows.
            progress = MathF.Min(1f, progress + (Dt / s_slash.Total));
            frames++;
            ActionState session = Slash(progress);
            presenter.Step(session, Vector2.UnitX, [], Dt, ImpactFeelOptions.Default);
            ActionState shown = WorldSceneRenderer.ShownAction(session, presenter);
            if (presenter.IsFrozen)
            {
                frozenShown = frozenShown < 0f ? shown.Progress : frozenShown;
                shown.Progress.Should().BeApproximately(frozenShown, 1e-4f, "the pose holds during the hit-stop");
                session.Progress.Should().BeGreaterThan(frozenShown - 1e-4f, "the session keeps running underneath");
            }

            shown.IsActive.Should().Be(session.IsActive, "the hit-stop never ends or extends the swing");
        }

        frozenShown.Should().BeGreaterThanOrEqualTo(0f, "a hit-stop played");
        frames.Should().BeLessThanOrEqualTo((int)MathF.Ceiling((1f - impact + 0.01f) * s_slash.Total / Dt) + 1, "the swing lasts exactly as long as without a hit");
    }

    [Theory]
    [InlineData(1f, 50f, HitStop.MinSeconds)]
    [InlineData(10f, 50f, 0.07f)]
    [InlineData(50f, 50f, HitStop.MaxSeconds)]
    public void SecondsFor_HeavierHitsFreezeLonger(float damage, float maxHealth, float expected) =>
        HitStop.SecondsFor(damage, maxHealth).Should().BeApproximately(expected, 0.004f);

    [Fact]
    public void CameraOffset_ShakeSettingOff_IsAlwaysZero()
    {
        ImpactPresenter presenter = new();
        ImpactFeelOptions off = new(ScreenShake: false, ReduceFlashes: false);

        presenter.Step(Slash(0.6f), Vector2.UnitX, [HeroHitsSlime(50f, crit: true)], Dt, off);
        for (int i = 0; i < 10; i++)
        {
            presenter.CameraOffset(off).Should().Be(Vector2.Zero, "shake off means no camera offset at all");
            presenter.Step(Slash(0.6f + (i * 0.01f)), Vector2.UnitX, [], Dt, off);
        }

        presenter.Trauma.Should().Be(0f, "trauma does not build up while shake is off");
    }

    [Fact]
    public void TraumaShake_Disabled_GivesNoOffsetEvenAtFullTrauma()
    {
        TraumaShake shake = default;
        shake.Add(1f);
        shake.Update(Dt);

        shake.Offset(enabled: false).Should().Be(Vector2.Zero);
        shake.Offset(enabled: true).Length().Should().BePositive();
        shake.Trauma.Should().BeLessThan(1f, "trauma decays every frame");
    }

    [Fact]
    public void CameraOffset_ShakeOn_MovesThenSettles()
    {
        ImpactPresenter presenter = new();
        ImpactFeelOptions on = ImpactFeelOptions.Default;
        presenter.Step(Slash(0.6f), Vector2.UnitX, [HeroHitsSlime(50f, crit: true)], Dt, on);

        float largest = 0f;
        for (int i = 0; i < 6; i++)
        {
            presenter.Step(Slash(0.6f), Vector2.UnitX, [], Dt, on);
            largest = MathF.Max(largest, presenter.CameraOffset(on).Length());
        }

        largest.Should().BeGreaterThan(0.2f).And.BeLessThanOrEqualTo(TraumaShake.MaxOffset * 1.5f);
        for (int i = 0; i < 60; i++)
        {
            presenter.Step(default, Vector2.UnitX, [], Dt, on);
        }

        presenter.CameraOffset(on).Should().Be(Vector2.Zero, "trauma decays to nothing within a second");
    }

    [Fact]
    public void Step_LocalHeroHit_ShakesWithoutHitStop()
    {
        ImpactPresenter presenter = new();
        StrikeImpactEvent hurt = new(Slime, Hero, Vector2.Zero, 20f, 100f, false, false, IsLocalAttacker: false, IsLocalVictim: true);

        presenter.Step(default, Vector2.UnitX, [hurt], Dt, ImpactFeelOptions.Default);

        presenter.Trauma.Should().BePositive();
        presenter.IsFrozen.Should().BeFalse("only the attacker's own swing freezes");
    }

    [Fact]
    public void Step_HitBetweenOthers_LeavesItToTheSession()
    {
        ImpactPresenter presenter = new();
        StrikeImpactEvent others = new(99UL, Slime, Vector2.Zero, 20f, 100f, false, false, false, false);

        presenter.Step(default, Vector2.UnitX, [others], Dt, ImpactFeelOptions.Default);

        presenter.Trauma.Should().Be(0f, "other players' hits do not shake this camera");
        presenter.FlashFor(Slime).Should().Be(0f, "the session flashes hits it did not deal");
        MultiplayerSession.FlashesDefenderOnArrival(99UL, Hero).Should().BeTrue();
        MultiplayerSession.FlashesDefenderOnArrival(Hero, Hero).Should().BeFalse("the local hero's hits flash at contact instead");
    }

    [Fact]
    public void ToStrikeImpact_OnlyDamagingHits()
    {
        MultiplayerSession.ToStrikeImpact(Hero, Slime, Hero, Vector2.Zero, 0f, 50f, false, false).Should().BeNull("a dodge has no impact");
        StrikeImpactEvent? hit = MultiplayerSession.ToStrikeImpact(Hero, Slime, Hero, Vector2.Zero, 12f, 50f, true, false);
        hit.Should().NotBeNull();
        hit!.Value.IsLocalAttacker.Should().BeTrue();
        hit.Value.IsLocalVictim.Should().BeFalse();
    }

    [Fact]
    public void ScaleFlash_ReduceFlashes_Softens()
    {
        new ImpactFeelOptions(ScreenShake: true, ReduceFlashes: true).ScaleFlash(0.8f).Should().BeApproximately(0.8f * ImpactFeelOptions.ReducedFlashScale, 1e-5f);
        ImpactFeelOptions.Default.ScaleFlash(0.8f).Should().Be(0.8f);
    }

    [Fact]
    public void Step_SteadyState_AllocatesNothing()
    {
        ImpactPresenter presenter = new();
        StrikeImpactEvent[] hits = [HeroHitsSlime()];
        presenter.Step(Slash(0.05f), Vector2.UnitX, hits, Dt, ImpactFeelOptions.Default);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int frame = 0; frame < 300; frame++)
        {
            float p = frame % 30 / 30f;
            ReadOnlySpan<StrikeImpactEvent> fresh = frame % 30 == 1 ? hits : [];
            presenter.Step(Slash(p), Vector2.UnitX, fresh, Dt, ImpactFeelOptions.Default);
            _ = presenter.CameraOffset(ImpactFeelOptions.Default);
            _ = presenter.FlashFor(Slime);
            _ = presenter.DisplayedProgress(Slash(p));
        }

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().Be(0, "impact feedback runs every frame");
    }

    [Fact]
    public void UiSettings_ShakeAndFlashSettings_RoundTrip()
    {
        string path = Path.Combine(Path.GetTempPath(), $"carbon-settings-{Guid.NewGuid():N}.ini");
        try
        {
            new UiSettings().ScreenShake.Should().BeTrue("shake is on by default");
            new UiSettings().ReduceFlashes.Should().BeFalse();
            new UiSettings { ScreenShake = false, ReduceFlashes = true }.Save(path);

            UiSettings loaded = UiSettings.LoadOrDefault(path);

            File.ReadAllText(path).Should().Contain("screen_shake = false").And.Contain("reduce_flashes = true");
            loaded.ScreenShake.Should().BeFalse();
            loaded.ReduceFlashes.Should().BeTrue();
        }
        finally
        {
            File.Delete(path);
        }
    }
}
