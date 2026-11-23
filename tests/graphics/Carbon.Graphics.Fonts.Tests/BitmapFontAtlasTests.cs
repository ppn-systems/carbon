// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Fonts.Atlases;
using Carbon.Graphics.Fonts.Model;
using Carbon.Graphics.OpenGL.Abstractions;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Carbon.Graphics.Fonts.Tests;

public sealed class BitmapFontAtlasTests
{
    [Fact]
    public void TryGetGlyph_WhenAsciiRegistered_RetrievesFromDenseTableInConstantTime()
    {
        // Arrange
        ITexture fakeTexture = Substitute.For<ITexture>();
        BitmapFontAtlas atlas = new(fakeTexture);
        FontGlyph glyphA = new('A', new RectangleF(10f, 10f, 16f, 20f), xOffset: 0f, yOffset: 2f, xAdvance: 18f);

        // Act
        atlas.AddGlyph(glyphA);
        bool found = atlas.TryGetGlyph('A', out FontGlyph result);

        // Assert
        found.Should().BeTrue();
        result.Character.Should().Be('A');
        result.XAdvance.Should().Be(18f);
        result.Bounds.Width.Should().Be(16f);
    }

    [Fact]
    public void TryGetGlyph_WhenMissingAndFallbackConfigured_ReturnsFallbackGlyph()
    {
        // Arrange
        ITexture fakeTexture = Substitute.For<ITexture>();
        BitmapFontAtlas atlas = new(fakeTexture)
        {
            FallbackGlyph = new('?', new RectangleF(0f, 0f, 10f, 15f), 0f, 0f, 12f)
        };

        // Act
        bool found = atlas.TryGetGlyph('Z', out FontGlyph result);

        // Assert
        found.Should().BeTrue();
        result.Character.Should().Be('?');
    }

    [Fact]
    public void AddKerning_WhenPairExists_ReturnsConfiguredOffset()
    {
        // Arrange
        ITexture fakeTexture = Substitute.For<ITexture>();
        BitmapFontAtlas atlas = new(fakeTexture);

        // Act
        atlas.AddKerning('A', 'V', -3.5f);

        // Assert
        atlas.GetKerning('A', 'V').Should().Be(-3.5f);
        atlas.GetKerning('V', 'A').Should().Be(0f);
    }
}
