// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Combat.Statuses.Config;
using Carbon.Desktop.Abstractions.StatusEffects;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.Hud;
using Carbon.Desktop.Rendering.World;
using Carbon.Graphics.Batching.Common;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>ADR-029 D10 hit feedback: the screen-edge vignette and the slow trail.</summary>
public sealed class StatusEffectFeedbackTests
{
    private static readonly StatusEffectCatalog s_shipped = StatusEffectCatalog.LoadShippedDefaults();

    [Fact]
    public void Resolve_JustHit_IsAFullRedVignetteThatOutranksPoison()
    {
        IWorldReadModel world = World(vignette: 0.45f, new LocalStatusEffect(1, 1, 5f, 10f, 1f, 2, 0));

        (ColorRgba color, float alpha) = DamageVignetteWidget.Resolve(world, animTime: 0f);

        color.Should().Be(new ColorRgba(200, 24, 24, 255));
        alpha.Should().Be(150f);
    }

    [Fact]
    public void Resolve_PoisonedOrSlowedOrNothing_PicksGreenBlueOrNone()
    {
        DamageVignetteWidget.Resolve(World(0f, new LocalStatusEffect(1, 1, 5f, 10f, 1f, 2, 0)), animTime: 0f)
            .Should().Be((new ColorRgba(96, 190, 40, 255), 55f), "poison pulses green");
        DamageVignetteWidget.Resolve(World(0f, new LocalStatusEffect(2, 1, 2f, 3f, 0.6f, 1, 101)), animTime: 0f)
            .Should().Be((new ColorRgba(120, 180, 240, 255), 50f), "slow is a cold edge");
        DamageVignetteWidget.Resolve(World(0f, new LocalStatusEffect(8, 1, 0f, 0f, 1.5f, 3, 0)), animTime: 0f).Alpha
            .Should().Be(0f, "a buff draws no vignette");
    }

    [Fact]
    public void Update_WhileSlowed_KeepsTheLastSixPositionsNewestFirstAndClearsWhenTheSlowEnds()
    {
        SlowTrail trail = new();
        for (int i = 0; i < 9; i++)
        {
            trail.Update(new Vector2(i * 10f, 0f), dt: 0.07f, slowed: true);
        }

        trail.Count.Should().Be(SlowTrail.Capacity);
        trail.Get(0).Should().Be(new Vector2(80f, 0f));
        trail.Get(5).Should().Be(new Vector2(30f, 0f));

        trail.Update(new Vector2(90f, 0f), dt: 0.01f, slowed: true);
        trail.Get(0).Should().Be(new Vector2(80f, 0f), "samples are 0.07 s apart");

        trail.Update(new Vector2(90f, 0f), dt: 0.07f, slowed: false);
        trail.Count.Should().Be(0);
    }

    private static IWorldReadModel World(float vignette, params LocalStatusEffect[] effects)
    {
        IWorldReadModel world = Substitute.For<IWorldReadModel>();
        world.LocalDamageVignetteTimer.Returns(vignette);
        world.LocalStatusEffects.Returns(effects);
        world.StatusEffectDefinitions.Returns(s_shipped);
        return world;
    }
}
