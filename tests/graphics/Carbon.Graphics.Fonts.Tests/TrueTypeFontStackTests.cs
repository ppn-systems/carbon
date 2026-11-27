// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Linq;
using Carbon.Graphics.Fonts.TrueType;
using FluentAssertions;
using Xunit;

namespace Carbon.Graphics.Fonts.Tests;

/// <summary>Multi-face rasterization and Unicode range baking for Vietnamese text (ADR-020 D5).</summary>
public sealed class TrueTypeFontStackTests
{
    private const string VietnameseSample = "ăâđêôơưĂÂĐÊÔƠƯếỡữặỢ";

    [Fact]
    public void HasGlyph_ThaleahLacksVietnameseButVt323CoversIt()
    {
        byte[] thaleah = ReadFont("thaleah_fat.ttf");
        byte[] vt323 = ReadFont("vt323.ttf");

        HasGlyph(thaleah, 'A').Should().BeTrue();
        HasGlyph(thaleah, 'ế').Should().BeFalse("the pixel font has no Vietnamese diacritics");
        foreach (char ch in VietnameseSample)
        {
            HasGlyph(vt323, ch).Should().BeTrue($"VT323 must cover '{ch}' (U+{(int)ch:X4})");
        }

        for (int cp = 0x1EA0; cp <= 0x1EF9; cp++)
        {
            HasGlyph(vt323, cp).Should().BeTrue($"VT323 must cover U+{cp:X4}");
        }
    }

    [Fact]
    public void Rasterize_WithFallbackFace_FillsGlyphsThePrimaryFaceLacks()
    {
        FontFaceSource[] faces =
        [
            new("thaleah_fat.ttf", ReadFont("thaleah_fat.ttf"), 16f),
            new("vt323.ttf", ReadFont("vt323.ttf"), 12.5f),
        ];

        TrueTypeRasterizedFont font = TrueTypeFontLoader.Rasterize("en", faces, UnicodeGlyphRanges.LatinFull);
        TrueTypeRasterizedFont primaryOnly = TrueTypeFontLoader.RasterizeAscii("ThaleahFat", faces[0].Data, 16f);

        foreach (char ch in VietnameseSample)
        {
            RasterizedGlyphInfo glyph = font.Glyphs.Should().ContainSingle(g => g.CodePoint == ch, $"'{ch}' comes from the fallback face").Which;
            glyph.Bounds.Width.Should().BeGreaterThan(0f);
            glyph.Bounds.Height.Should().BeGreaterThan(0f);
        }

        font.Metrics.LineHeight.Should().Be(primaryOnly.Metrics.LineHeight, "the primary face defines line metrics");
        font.Metrics.BaseLine.Should().Be(primaryOnly.Metrics.BaseLine);
        font.AtlasWidth.Should().Be(512u, "more than 256 glyphs widen the atlas");
        font.Glyphs.Select(g => g.CodePoint).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Rasterize_VietnameseStack_DrawsDiacriticsAboveTheBaseLetter()
    {
        TrueTypeRasterizedFont font = TrueTypeFontLoader.Rasterize(
            "vi",
            [new FontFaceSource("vt323.ttf", ReadFont("vt323.ttf"), 20f)],
            UnicodeGlyphRanges.LatinFull);

        RasterizedGlyphInfo plain = font.Glyphs.Single(g => g.CodePoint == 'e');
        RasterizedGlyphInfo toned = font.Glyphs.Single(g => g.CodePoint == 'ế');

        toned.Bounds.Height.Should().BeGreaterThan(plain.Bounds.Height, "the circumflex and acute add height");
        toned.YOffset.Should().BeLessThan(plain.YOffset, "marks extend above the x-height");
        toned.XAdvance.Should().Be(plain.XAdvance, "a monospace pixel face keeps the advance");
    }

    private static bool HasGlyph(byte[] font, int codePoint) => TrueTypeFontLoader.HasGlyph(font, codePoint);

    private static byte[] ReadFont(string fileName)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "resources", "fonts")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("test must locate repository resources/fonts directory");
        return File.ReadAllBytes(Path.Combine(dir.FullName, "resources", "fonts", fileName));
    }
}
