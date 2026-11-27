// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Fonts.Atlases;
using Carbon.Graphics.Fonts.Layout;
using Carbon.Graphics.Fonts.Model;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Graphics.OpenGL.Abstractions;
using Carbon.Testing;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>Audit round 2, PERF-9: <see cref="CarbonUiSkin.TruncateToWidth"/> no longer builds a string per step.</summary>
public sealed class TruncateToWidthTests
{
    private static readonly BitmapFont s_font = CreateFont();

    [Fact]
    public void TruncateToWidth_ManyTextsAndWidths_MatchesTheStringPerStepAlgorithm()
    {
        string[] texts = ["Iron Sword", "Crystal Staff of the Ancient Mage", "WWWWWWWWWWWWWW", "iiiiiiiiiiiiiiiiiiii", "AV", "A", new string('m', 300)];
        foreach (string text in texts)
        {
            for (float width = 0f; width <= 420f; width += 3.5f)
            {
                foreach (float scale in new[] { 1f, 1.25f })
                {
                    CarbonUiSkin.TruncateToWidth(s_font, text, width, scale).Should().Be(
                        Reference(s_font, text, width, scale),
                        $"'{text}' at width {width}, scale {scale} must truncate exactly as before");
                }
            }
        }
    }

    [Fact]
    public void TruncateToWidth_OverlongLabel_AllocatesOnlyTheResult()
    {
        string text = "Crystal Staff of the Ancient Mage, Tier Five";
        _ = CarbonUiSkin.TruncateToWidth(s_font, text, 90f);

        AllocationWindow open = AllocationProbe.Start();
        string result = CarbonUiSkin.TruncateToWidth(s_font, text, 90f);
        AllocationResult window = open.Stop(1);

        long resultBytes = 22 + (2L * result.Length) + 8;
        window.Bytes.Should().BeLessThanOrEqualTo(resultBytes, $"only the returned string may be allocated (it was one string per step) ({window})");
    }

    // The implementation before PERF-9: one string.Concat per candidate length.
    private static string Reference(BitmapFont font, string text, float maxWidth, float scale)
    {
        if (string.IsNullOrEmpty(text) || maxWidth <= 8f)
        {
            return string.Empty;
        }

        if (font.MeasureString(text, TextLayoutOptions.Default, scale).Width <= maxWidth)
        {
            return text;
        }

        const string ellipsis = "..";
        for (int len = text.Length - 1; len > 0; len--)
        {
            string candidate = string.Concat(text.AsSpan(0, len), ellipsis);
            if (font.MeasureString(candidate, TextLayoutOptions.Default, scale).Width <= maxWidth)
            {
                return candidate;
            }
        }

        return ellipsis;
    }

    private static BitmapFont CreateFont()
    {
        BitmapFontAtlas atlas = new(Substitute.For<ITexture>());
        for (char c = ' '; c <= '~'; c++)
        {
            // Uneven advances so the cut point varies with the text.
            float advance = c is 'i' or 'l' or '.' or ',' ? 4f : (c is 'm' or 'w' or 'M' or 'W' ? 13f : 8f + (c % 3));
            atlas.AddGlyph(new FontGlyph(c, new RectangleF(0f, 0f, advance, 16f), 0f, 0f, advance));
        }

        // Kerning against the ellipsis, the case where measuring parts separately would differ.
        atlas.AddKerning('e', '.', -2f);
        atlas.AddKerning('V', '.', -3f);
        atlas.AddKerning('A', 'V', -2f);
        return new BitmapFont("test", atlas, new FontMetrics(fontSize: 16f, lineHeight: 20f, baseLine: 16f, spaceWidth: 6f));
    }
}
