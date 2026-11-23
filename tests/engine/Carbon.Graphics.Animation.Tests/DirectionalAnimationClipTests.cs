// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Graphics.Animation.Clips;
using Carbon.Graphics.Animation.Directions;
using Carbon.Graphics.Batching.Common;
using FluentAssertions;
using Xunit;

namespace Carbon.Graphics.Animation.Tests;

public sealed class DirectionalAnimationClipTests
{
    private static AnimationClip MakeDummyClip(string name) =>
        new(name, [new AnimationFrame(default, 0.1f)]);

    [Fact]
    public void Constructor_With8Directions_ShouldMapAllDirectionsCorrectly()
    {
        AnimationClip s = MakeDummyClip("south");
        AnimationClip se = MakeDummyClip("southeast");
        AnimationClip e = MakeDummyClip("east");
        AnimationClip ne = MakeDummyClip("northeast");
        AnimationClip n = MakeDummyClip("north");
        AnimationClip nw = MakeDummyClip("northwest");
        AnimationClip w = MakeDummyClip("west");
        AnimationClip sw = MakeDummyClip("southwest");

        DirectionalAnimationClip dirClip = new("walk", s, se, e, ne, n, nw, w, sw);

        dirClip.Name.Should().Be("walk");

        dirClip.GetClip(Direction8.South, out SpriteEffects fxS).Should().BeSameAs(s);
        fxS.Should().Be(SpriteEffects.None);

        dirClip.GetClip(Direction8.North, out SpriteEffects fxN).Should().BeSameAs(n);
        fxN.Should().Be(SpriteEffects.None);

        dirClip.GetClip(Direction8.West, out SpriteEffects fxW).Should().BeSameAs(w);
        fxW.Should().Be(SpriteEffects.None);

        dirClip.GetClip(Direction8.NorthWest, out SpriteEffects fxNW).Should().BeSameAs(nw);
        fxNW.Should().Be(SpriteEffects.None);
    }

    [Fact]
    public void Create5Direction_ShouldAutoFlipWestVariants()
    {
        AnimationClip s = MakeDummyClip("south");
        AnimationClip se = MakeDummyClip("southeast");
        AnimationClip e = MakeDummyClip("east");
        AnimationClip ne = MakeDummyClip("northeast");
        AnimationClip n = MakeDummyClip("north");

        DirectionalAnimationClip dirClip = DirectionalAnimationClip.Create5Direction("idle", s, se, e, ne, n);

        // East should not be flipped
        dirClip.GetClip(Direction8.East, out SpriteEffects fxE).Should().BeSameAs(e);
        fxE.Should().Be(SpriteEffects.None);

        // West should use East clip with FlipHorizontally
        dirClip.GetClip(Direction8.West, out SpriteEffects fxW).Should().BeSameAs(e);
        fxW.Should().Be(SpriteEffects.FlipHorizontally);

        // NorthWest should use NorthEast clip with FlipHorizontally
        dirClip.GetClip(Direction8.NorthWest, out SpriteEffects fxNW).Should().BeSameAs(ne);
        fxNW.Should().Be(SpriteEffects.FlipHorizontally);

        // SouthWest should use SouthEast clip with FlipHorizontally
        dirClip.GetClip(Direction8.SouthWest, out SpriteEffects fxSW).Should().BeSameAs(se);
        fxSW.Should().Be(SpriteEffects.FlipHorizontally);

        // South and North remain unflipped
        dirClip.GetClip(Direction8.South, out SpriteEffects fxS).Should().BeSameAs(s);
        fxS.Should().Be(SpriteEffects.None);
        dirClip.GetClip(Direction8.North, out SpriteEffects fxN).Should().BeSameAs(n);
        fxN.Should().Be(SpriteEffects.None);
    }

    [Fact]
    public void CreateSingle_ShouldMapSameClipToAllDirections()
    {
        AnimationClip clip = MakeDummyClip("spell");
        DirectionalAnimationClip dirClip = DirectionalAnimationClip.CreateSingle("spell_spin", clip);

        foreach (Direction8 dir in Enum.GetValues<Direction8>())
        {
            dirClip.GetClip(dir, out SpriteEffects fx).Should().BeSameAs(clip);
            fx.Should().Be(SpriteEffects.None);
        }
    }
}
