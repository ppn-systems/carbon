// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Graphics.Animation.Atlas;
using Carbon.Graphics.Animation.Clips;
using Carbon.Graphics.Animation.Controllers;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.OpenGL.Abstractions;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Carbon.Graphics.Animation.Tests;

public sealed class SpriteAnimatorTests
{
    [Fact]
    public void Play_LoopingClip_AdvancesFramesDeterministicallyOverTime()
    {
        // Arrange
        ITexture fakeTexture = Substitute.For<ITexture>();
        AtlasRegion r0 = new(fakeTexture, new RectangleF(0f, 0f, 32f, 32f), "f0");
        AtlasRegion r1 = new(fakeTexture, new RectangleF(32f, 0f, 32f, 32f), "f1");

        AnimationClip clip = new(
            "walk",
            [new AnimationFrame(r0, 0.1f), new AnimationFrame(r1, 0.1f)],
            AnimationPlaybackMode.Loop);

        SpriteAnimator animator = new();
        animator.Play(clip);

        // Act & Assert
        animator.CurrentFrameIndex.Should().Be(0);

        animator.Update(0.05f); // 50ms (still frame 0)
        animator.CurrentFrameIndex.Should().Be(0);

        animator.Update(0.06f); // 110ms total (should advance to frame 1)
        animator.CurrentFrameIndex.Should().Be(1);

        animator.Update(0.1f); // 210ms total (should wrap back to frame 0)
        animator.CurrentFrameIndex.Should().Be(0);
    }

    [Fact]
    public void Play_PlayOnceClip_FiresCompletedEventAndStopsAtEnd()
    {
        // Arrange
        ITexture fakeTexture = Substitute.For<ITexture>();
        AtlasRegion r0 = new(fakeTexture, new RectangleF(0f, 0f, 32f, 32f), "f0");
        AtlasRegion r1 = new(fakeTexture, new RectangleF(32f, 0f, 32f, 32f), "f1");

        AnimationClip clip = new(
            "attack",
            [new AnimationFrame(r0, 0.1f), new AnimationFrame(r1, 0.1f)],
            AnimationPlaybackMode.Once);

        SpriteAnimator animator = new();
        bool completedFired = false;
        animator.Completed += (c) => completedFired = true;

        animator.Play(clip);

        // Act: Advance past total duration (0.2s)
        animator.Update(0.25f);

        // Assert
        completedFired.Should().BeTrue();
        animator.CurrentFrameIndex.Should().Be(1);
    }
}
