// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Graphics.Animation.Atlas;
using Carbon.Graphics.OpenGL.Abstractions;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Carbon.Graphics.Animation.Tests;

public sealed class SpriteAtlasTests
{
    [Fact]
    public void CreateGrid_WhenTextureProvided_SlicesIntoUniformRegions()
    {
        // Arrange: 128x64 texture sliced into 32x32 cells (4 columns x 2 rows = 8 frames)
        ITexture fakeTexture = Substitute.For<ITexture>();
        fakeTexture.Width.Returns(128u);
        fakeTexture.Height.Returns(64u);

        // Act
        using SpriteAtlas atlas = SpriteAtlas.CreateGrid(fakeTexture, cellWidth: 32, cellHeight: 32, prefix: "hero_walk");

        // Assert
        atlas.Count.Should().Be(8);
        atlas.Contains("hero_walk_0").Should().BeTrue();
        atlas.Contains("hero_walk_7").Should().BeTrue();

        AtlasRegion region0 = atlas["hero_walk_0"];
        region0.Bounds.X.Should().Be(0f);
        region0.Bounds.Y.Should().Be(0f);
        region0.Bounds.Width.Should().Be(32f);
        region0.Bounds.Height.Should().Be(32f);

        AtlasRegion region5 = atlas["hero_walk_5"]; // col 1, row 1
        region5.Bounds.X.Should().Be(32f);
        region5.Bounds.Y.Should().Be(32f);
    }

    [Fact]
    public void SpriteAtlas_Dispose_CascadesDisposeToUnderlyingTexture()
    {
        // Arrange
        ITexture fakeTexture = Substitute.For<ITexture>();
        SpriteAtlas atlas = new(fakeTexture);

        // Act
        atlas.Dispose();

        // Assert
        fakeTexture.Received(1).Dispose();
    }
}
