// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.OpenGL.Abstractions;
using Carbon.Graphics.Scenes.Objects;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Carbon.Graphics.Scenes.Tests;

public sealed class VisualObjectsTests
{
    [Fact]
    public void SpriteObject_Bounds_ShouldCalculateWithOriginAndScale()
    {
        ITexture fakeTexture = Substitute.For<ITexture>();
        fakeTexture.Width.Returns(32u);
        fakeTexture.Height.Returns(32u);

        SpriteObject sprite = new(fakeTexture)
        {
            Position = new Vector2(100f, 100f),
            Scale = new Vector2(2f, 2f),
            Origin = new Vector2(16f, 16f)
        };

        // Expected:
        // scaledW = 32 * 2 = 64
        // scaledH = 32 * 2 = 64
        // X = 100 - (16 * 2) = 68
        // Y = 100 - (16 * 2) = 68
        RectangleF bounds = sprite.Bounds;
        bounds.X.Should().Be(68f);
        bounds.Y.Should().Be(68f);
        bounds.Width.Should().Be(64f);
        bounds.Height.Should().Be(64f);
    }

    [Fact]
    public void SpriteObject_Bounds_WithSourceRectangle_ShouldUseSourceDimensions()
    {
        ITexture fakeTexture = Substitute.For<ITexture>();
        fakeTexture.Width.Returns(128u);
        fakeTexture.Height.Returns(128u);

        RectangleF src = new(0f, 0f, 16f, 16f);
        SpriteObject sprite = new(fakeTexture, src)
        {
            Position = new Vector2(50f, 50f),
            Scale = Vector2.One,
            Origin = Vector2.Zero
        };

        RectangleF bounds = sprite.Bounds;
        bounds.X.Should().Be(50f);
        bounds.Y.Should().Be(50f);
        bounds.Width.Should().Be(16f);
        bounds.Height.Should().Be(16f);
    }
}
