// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.Effects;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Desktop.Rendering.World;
using Carbon.Graphics.Animation.Directions;
using Carbon.Items.Catalog;
using Carbon.Testing;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>Weapon trail along the tip path (docs/art/combat-animation.md §3.5).</summary>
public sealed class WeaponTrailTests
{
    private static readonly Lazy<CarbonVectorArtLibrary> s_art = new(() =>
        CarbonVectorArtLibrary.LoadFromDirectory(VectorArtValidator.TryFindArtRootDirectory() ?? throw new InvalidOperationException("art root not found")));

    private static CompiledVectorRig Hero => s_art.Value.DefaultCharacterRig ?? throw new InvalidOperationException("hero");

    private static CompiledItemVectorArt Item(uint id) => s_art.Value.TryGetItem(id, out CompiledItemVectorArt? art) ? art : throw new InvalidOperationException($"item {id}");

    private static ActionState Swing(ActionAnimationKind kind, uint itemId, byte combo, float progress)
    {
        float total = HeroAttackTiming.For(HeroAttackTiming.StyleOf(kind, combo)).Total;
        return new ActionState(kind, total, total * (1f - progress), Vector2.UnitX, itemId, combo);
    }

    [Fact]
    public void Window_ShowsOnlyFromStrikeThroughActivePlusFade()
    {
        HeroAttackTiming timing = HeroAttackTiming.For(HeroAttackStyle.SlashForehand);
        float fade = WeaponTrailRenderer.FadeSeconds / timing.Total;

        WeaponTrailRenderer.Window(in timing, timing.ContactFraction * 0.5f, out _, out _).Should().Be(0f, "no trail while winding up");
        WeaponTrailRenderer.Window(in timing, timing.ContactFraction + 0.01f, out float tail, out float head).Should().Be(1f);
        head.Should().BeApproximately(timing.ContactFraction + 0.01f, 1e-5f, "the newest sample is the current frame");
        tail.Should().BeLessThan(timing.ContactFraction, "the ribbon starts as the strike leaves the cocked pose");
        float fading = WeaponTrailRenderer.Window(in timing, timing.ActiveEndFraction + (fade * 0.5f), out float fadeTail, out float fadeHead);
        fading.Should().BeApproximately(0.5f, 1e-3f);
        fadeTail.Should().BeGreaterThan(tail, "the tail catches up with the head while fading");
        fadeHead.Should().BeApproximately(timing.ActiveEndFraction, 1e-5f);
        WeaponTrailRenderer.Window(in timing, timing.ActiveEndFraction + fade + 1e-3f, out _, out _).Should().Be(0f, "gone in recovery");
    }

    [Theory]
    [InlineData(ActionAnimationKind.SwordSlash, StarterGameCatalog.IronSword, 0)]
    [InlineData(ActionAnimationKind.SwordSlash, StarterGameCatalog.IronSword, 1)]
    [InlineData(ActionAnimationKind.AxeChop, StarterGameCatalog.WoodAxe, 0)]
    [InlineData(ActionAnimationKind.StaffCast, StarterGameCatalog.ArcaneStaff, 0)]
    public void SamplePath_NewestSample_IsTheDrawnWeaponTip(ActionAnimationKind kind, uint itemId, byte combo)
    {
        CompiledVectorRig rig = Hero;
        CompiledItemVectorArt item = Item(itemId);
        HeroAttackStyle style = HeroAttackTiming.StyleOf(kind, combo);
        HeroAttackTiming timing = HeroAttackTiming.For(style);
        ActionState action = Swing(kind, itemId, combo, HeroAttackPose.ImpactFraction(in timing));
        Vector2 foot = new(100f, 200f);
        Span<Vector2> inner = stackalloc Vector2[WeaponTrailRenderer.Samples + 1];
        Span<Vector2> outer = stackalloc Vector2[WeaponTrailRenderer.Samples + 1];

        float opacity = WeaponTrailRenderer.SamplePath(rig, item, foot, Direction8.East, in action, inner, outer, out CompiledItemVectorArt? weapon);

        opacity.Should().Be(1f);
        weapon.Should().BeSameAs(item);
        Span<BonePose> bones = stackalloc BonePose[(int)VectorBoneId.Count];
        rig.EvaluatePose(HeroAttackPose.ClipName(style)!, "idle", action.Progress, isNormalized: true, bones);
        HeldItemPose drawn = VectorRigRenderer.ComputeHeldItemPose(
            rig, item, foot + WorldSceneRenderer.AttackLean(in action), Direction8.East, rig.WorldHeight / rig.DesignHeight, mirrorX: false, in bones[(int)VectorBoneId.ArmFront], in action, 0f);
        Vector2.Distance(outer[^1], drawn.ToWorld(item.TipPoint)).Should().BeLessThan(1e-3f, "the ribbon ends exactly at the tip drawn this frame");

        float travelled = 0f;
        for (int i = 1; i < outer.Length; i++)
        {
            travelled += Vector2.Distance(outer[i - 1], outer[i]);
        }

        travelled.Should().BeGreaterThan(8f, $"{style} sweeps the tip through an arc");
    }

    [Theory]
    [InlineData(StarterGameCatalog.IronSword)]
    [InlineData(StarterGameCatalog.WoodAxe)]
    [InlineData(StarterGameCatalog.StonePickaxe)]
    public void TipPoint_IsFarFromTheGrip(uint itemId)
    {
        CompiledItemVectorArt item = Item(itemId);

        Vector2.Distance(item.TipPoint, item.GripPoint).Should().BeGreaterThan(8f, "the trail follows the far end of the weapon");
    }

    [Fact]
    public void SamplePath_BareHands_FollowsTheKnuckles()
    {
        ActionState jab = Swing(ActionAnimationKind.Punch, 0U, 0, HeroAttackPose.ImpactFraction(HeroAttackTiming.For(HeroAttackStyle.Jab)));
        Span<Vector2> inner = stackalloc Vector2[WeaponTrailRenderer.Samples + 1];
        Span<Vector2> outer = stackalloc Vector2[WeaponTrailRenderer.Samples + 1];

        float opacity = WeaponTrailRenderer.SamplePath(Hero, Item(StarterGameCatalog.IronSword), Vector2.Zero, Direction8.East, in jab, inner, outer, out CompiledItemVectorArt? weapon);

        opacity.Should().BePositive();
        weapon.Should().BeNull("a punch draws a knuckle streak, never a blade ribbon");
        outer[^1].X.Should().BeGreaterThan(outer[0].X, "the jab streak moves toward the target (east)");
    }

    [Theory]
    [InlineData(0, 248, 250, 252)]
    [InlineData(3, 250, 204, 21)]
    [InlineData(7, 34, 211, 238)]
    public void TrailColours_EnhancementSetsTheEdge(byte upgrade, byte r, byte g, byte b)
    {
        (_, Carbon.Graphics.Batching.Common.ColorRgba edge) = WeaponTrailRenderer.TrailColours(Item(StarterGameCatalog.IronSword), upgrade);

        (edge.R, edge.G, edge.B).Should().Be((r, g, b));
    }

    [Fact]
    public void ReleaseVfxProgress_MeleeUsesTheTrail_RangedStartsAtRelease()
    {
        WorldSceneRenderer.ReleaseVfxProgress(ActionAnimationKind.SwordSlash, 0, 0.4f).Should().BeNull("the old body ribbon is replaced by the weapon trail");
        WorldSceneRenderer.ReleaseVfxProgress(ActionAnimationKind.Punch, 1, 0.4f).Should().BeNull();
        float release = HeroAttackTiming.For(HeroAttackStyle.BowShot).ContactFraction;
        WorldSceneRenderer.ReleaseVfxProgress(ActionAnimationKind.BowShot, 0, release * 0.5f).Should().BeNull("no release streak while drawing");
        WorldSceneRenderer.ReleaseVfxProgress(ActionAnimationKind.BowShot, 0, release).Should().Be(0f);
        WorldSceneRenderer.ReleaseVfxProgress(ActionAnimationKind.SpinSlash, 0, 0.4f).Should().Be(0.4f, "skills keep their VFX timing");
    }

    [Fact]
    public void SamplePath_SteadyState_AllocatesNothing()
    {
        CompiledVectorRig rig = Hero;
        CompiledItemVectorArt item = Item(StarterGameCatalog.IronSword);
        ActionState action = Swing(ActionAnimationKind.SwordSlash, StarterGameCatalog.IronSword, 0, 0.3f);

        AllocationResult window = AllocationProbe.Measure(200, warmupIterations: 200, frame =>
        {
            Span<Vector2> inner = stackalloc Vector2[WeaponTrailRenderer.Samples + 1];
            Span<Vector2> outer = stackalloc Vector2[WeaponTrailRenderer.Samples + 1];
            ActionState at = action with { RemainingSeconds = action.DurationSeconds * (1f - (frame / 200f)) };
            _ = WeaponTrailRenderer.SamplePath(rig, item, Vector2.Zero, Direction8.East, in at, inner, outer, out _);
        });

        window.Bytes.Should().Be(0, $"the trail is sampled every frame ({window})");
    }
}
