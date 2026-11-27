// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Desktop.Rendering.Atmospherics;
using Carbon.Desktop.Rendering.World;
using Carbon.Testing;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>
/// Ambient life (docs/art/graphics-quality.md): bird flocks cross the view by day, with ground shadows, and keep to
/// clear daylight.
/// </summary>
public sealed class AmbientLifeTests
{
    private static readonly Vector2 s_min = new(1000f, 2000f);
    private static readonly Vector2 s_max = new(1640f, 2360f);

    [Fact]
    public void DayActivity_ClearNoon_IsFull_NightAndStormAreEmpty()
    {
        AmbientLife.DayActivity(0f, 0f).Should().Be(1f);
        AmbientLife.DayActivity(1f, 0f).Should().Be(0f, "birds roost at night (fireflies take over)");
        AmbientLife.DayActivity(0f, 1f).Should().Be(0f, "and shelter from heavy rain");
    }

    [Fact]
    public void TryGetBird_OverAnHour_FlocksComeAndGo()
    {
        // Over an hour, a slot carries a flock across the view for a minority of the time: some cycles stay empty.
        int framesWithBirds = 0;
        const int Frames = 36000;
        for (int f = 0; f < Frames; f++)
        {
            framesWithBirds += AmbientLife.TryGetBird(0, 0, f * 0.1f, s_min, s_max, out _) ? 1 : 0;
        }

        float share = framesWithBirds / (float)Frames;
        share.Should().BeGreaterThan(0.15f, "a flock crosses now and then");
        share.Should().BeLessThan(0.5f, "but the sky is not always full");
    }

    [Fact]
    public void TryGetBird_InFlight_MovesAlongItsHeadingAndShadowsBelow()
    {
        (int slot, float t) = FindFlight();

        AmbientLife.TryGetBird(slot, 0, t, s_min, s_max, out AmbientLife.Bird a).Should().BeTrue();
        AmbientLife.TryGetBird(slot, 0, t + 1f, s_min, s_max, out AmbientLife.Bird b).Should().BeTrue();

        Vector2.Dot(b.Position - a.Position, a.Heading).Should().BeGreaterThan(20f, "birds fly forward");
        a.Shadow.Y.Should().BeGreaterThan(a.Position.Y, "a high bird's shadow falls below it on the ground");
    }

    [Fact]
    public void TryGetBird_Flock_FliesInFormationBehindTheLeader()
    {
        (int slot, float t) = FindFlight();
        AmbientLife.TryGetBird(slot, 0, t, s_min, s_max, out AmbientLife.Bird leader).Should().BeTrue();

        for (int i = 1; i < AmbientLife.BirdsPerFlock; i++)
        {
            AmbientLife.TryGetBird(slot, i, t, s_min, s_max, out AmbientLife.Bird follower).Should().BeTrue();
            Vector2.Dot(follower.Position - leader.Position, leader.Heading).Should().BeLessThan(0f, $"bird {i} trails the leader");
            Vector2.Distance(follower.Position, leader.Position).Should().BeLessThan(60f, "the flock stays together");
        }
    }

    [Fact]
    public void TryGetBird_EveryFrame_AllocatesNothing()
    {
        AllocationResult window = AllocationProbe.Measure(3000, warmupIterations: 3000, i =>
            _ = AmbientLife.TryGetBird(i % AmbientLife.FlockSlots, i % AmbientLife.BirdsPerFlock, i * 0.05f, s_min, s_max, out _));

        window.Bytes.Should().Be(0, $"every bird is placed every frame ({window})");
    }

    [Fact]
    public void LifeClock_ServerOwnsTheHour_KeepsBirdsFlying()
    {
        // Online the server owns the hour and the weather clock stands still (#223); birds must not freeze with it.
        Atmosphere atmosphere = new(11f, 1440f, forcedRain: false);
        WorldSceneRenderer.AdvanceAtmosphere(atmosphere, 11f, 1f / 60f, 1f);
        float before = atmosphere.LifeClock;

        for (int frame = 0; frame < 60; frame++)
        {
            WorldSceneRenderer.AdvanceAtmosphere(atmosphere, 11f, 1f / 60f, 1f);
        }

        (atmosphere.LifeClock - before).Should().BeApproximately(1f, 1e-3f, "a second of online frames moves the flocks a second on");
    }

    // A flock slot and a time at which its leader has been flying for a while and keeps flying for 1 s more.
    private static (int Slot, float Time) FindFlight()
    {
        for (int slot = 0; slot < AmbientLife.FlockSlots; slot++)
        {
            for (float t = 0f; t < 600f; t += 0.5f)
            {
                if (AmbientLife.TryGetBird(slot, 0, t, s_min, s_max, out _) &&
                    AmbientLife.TryGetBird(slot, 0, t + 1f, s_min, s_max, out _))
                {
                    return (slot, t);
                }
            }
        }

        throw new InvalidOperationException("no flock found");
    }
}
