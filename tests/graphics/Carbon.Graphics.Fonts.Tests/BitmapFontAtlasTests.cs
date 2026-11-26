// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Fonts.Atlases;
using Carbon.Graphics.Fonts.Model;
using Carbon.Graphics.Fonts.TrueType;
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

    [Fact]
    public void RasterizeAscii_WithRepositoryThaleahFont_ProducesAllPrintableAsciiGlyphs()
    {
        // Arrange
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "resources", "fonts")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("test must locate repository resources/fonts directory");
        string fontsDir = Path.Combine(dir!.FullName, "resources", "fonts");
        string[] fontFiles = Directory.GetFiles(fontsDir);
        fontFiles.Should().ContainSingle(path => string.Equals(Path.GetFileName(path), "thaleah_fat.ttf", StringComparison.OrdinalIgnoreCase),
            "unused fonts should be removed leaving only thaleah_fat.ttf");

        byte[] ttfBytes = File.ReadAllBytes(fontFiles[0]);

        // Act
        TrueTypeRasterizedFont font = TrueTypeFontLoader.RasterizeAscii("ThaleahFat", ttfBytes, 16f);

        // Assert
        font.AtlasWidth.Should().BeGreaterThan(0);
        font.AtlasHeight.Should().BeGreaterThan(0);
        font.RgbaPixels.Length.Should().Be((int)(font.AtlasWidth * font.AtlasHeight * 4));
        font.Metrics.LineHeight.Should().BeGreaterThan(0f);
        font.Glyphs.Should().HaveCount(95);

        char[] sampleChars = ['A', 'g', '9', '%', '+'];
        foreach (char ch in sampleChars)
        {
            RasterizedGlyphInfo glyph = font.Glyphs.Should().ContainSingle(g => g.CodePoint == ch).Which;
            glyph.Bounds.Width.Should().BeGreaterThan(0f);
            glyph.Bounds.Height.Should().BeGreaterThan(0f);
            glyph.XAdvance.Should().BeGreaterThan(0f);

            int startX = (int)glyph.Bounds.X;
            int startY = (int)glyph.Bounds.Y;
            int width = (int)glyph.Bounds.Width;
            int height = (int)glyph.Bounds.Height;
            bool hasOpaquePixel = false;
            for (int y = startY; y < startY + height && !hasOpaquePixel; y++)
            {
                for (int x = startX; x < startX + width; x++)
                {
                    int alpha = font.RgbaPixels[(((y * (int)font.AtlasWidth) + x) * 4) + 3];
                    if (alpha > 0)
                    {
                        hasOpaquePixel = true;
                        break;
                    }
                }
            }

            hasOpaquePixel.Should().BeTrue($"glyph '{ch}' must have rasterized opaque pixels");
        }
    }
}
