// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Atlases;
using Carbon.Graphics.Fonts.Model;

namespace Carbon.Graphics.Fonts.Layout;

/// <summary>
/// High-performance, zero-allocation text measurement and glyph layout engine.
/// Operates directly on <see cref="ReadOnlySpan{T}"/> of characters to eliminate GC allocations.
/// </summary>
public static class TextLayoutEngine
{
    /// <summary>
    /// Measures the total pixel dimensions and line statistics of the provided text.
    /// Zero heap allocation.
    /// </summary>
    /// <param name="atlas">The font atlas containing glyph definitions.</param>
    /// <param name="metrics">The font-wide metric values.</param>
    /// <param name="text">The character span to measure.</param>
    /// <param name="options">Layout constraints and spacing options.</param>
    /// <param name="scale">Scale multiplier (default 1.0).</param>
    /// <returns>A <see cref="TextMeasureResult"/> with the measured width, height, and line count.</returns>
    public static TextMeasureResult Measure(
        BitmapFontAtlas atlas,
        in FontMetrics metrics,
        ReadOnlySpan<char> text,
        in TextLayoutOptions options,
        float scale = 1.0f)
    {
        ArgumentNullException.ThrowIfNull(atlas, nameof(atlas));

        if (text.IsEmpty || scale <= 0f)
        {
            return new TextMeasureResult(0f, 0f, 0);
        }

        float scaledSpace = metrics.SpaceWidth * scale;
        float scaledTab = metrics.TabWidth * scale;
        float scaledLetterSpacing = options.LetterSpacing * scale;

        float maxLineWidth = 0f;
        float currentLineWidth = 0f;
        int lineCount = 1;

        int i = 0;
        int lineStart = 0;
        int lastSpaceIndex = -1;
        float widthAtLastSpace = 0f;
        char prevChar = '\0';

        while (i < text.Length)
        {
            char c = text[i];

            if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                maxLineWidth = Math.Max(maxLineWidth, currentLineWidth);
                currentLineWidth = 0f;
                lastSpaceIndex = -1;
                prevChar = '\0';
                lineCount++;
                i++;
                lineStart = i;
                continue;
            }

            if (c == '\n')
            {
                maxLineWidth = Math.Max(maxLineWidth, currentLineWidth);
                currentLineWidth = 0f;
                lastSpaceIndex = -1;
                prevChar = '\0';
                lineCount++;
                i++;
                lineStart = i;
                continue;
            }

            if (c == '\t')
            {
                currentLineWidth += scaledTab + scaledLetterSpacing;
                prevChar = '\t';
                i++;
                continue;
            }

            if (c == ' ')
            {
                lastSpaceIndex = i;
                widthAtLastSpace = currentLineWidth;
                currentLineWidth += scaledSpace + scaledLetterSpacing;
                prevChar = ' ';
                i++;
                continue;
            }

            float advance;
            if (atlas.TryGetGlyph(c, out FontGlyph glyph))
            {
                float kerning = prevChar != '\0' ? atlas.GetKerning(prevChar, c) : 0f;
                advance = ((glyph.XAdvance + kerning) * scale) + scaledLetterSpacing;
            }
            else
            {
                advance = scaledSpace + scaledLetterSpacing;
            }

            // Word wrap check against MaxWidth (in screen pixels)
            if (options.WordWrap && options.MaxWidth > 0f && (currentLineWidth + advance) > options.MaxWidth)
            {
                if (lastSpaceIndex >= lineStart)
                {
                    // Break at last space
                    maxLineWidth = Math.Max(maxLineWidth, widthAtLastSpace);
                    lineCount++;
                    currentLineWidth = 0f;
                    i = lastSpaceIndex + 1;
                    lineStart = i;
                    lastSpaceIndex = -1;
                    prevChar = '\0';
                    continue;
                }

                // If single word is wider than MaxWidth, force character break
                if (currentLineWidth > 0f)
                {
                    maxLineWidth = Math.Max(maxLineWidth, currentLineWidth);
                    lineCount++;
                    currentLineWidth = 0f;
                    lineStart = i;
                    lastSpaceIndex = -1;
                    prevChar = '\0';
                    continue;
                }
            }

            currentLineWidth += advance;
            prevChar = c;
            i++;
        }

        maxLineWidth = Math.Max(maxLineWidth, currentLineWidth);

        float totalHeight = ((lineCount * metrics.LineHeight)
            + (Math.Max(0, lineCount - 1) * options.LineSpacing)) * scale;

        return new TextMeasureResult(maxLineWidth, totalHeight, lineCount);
    }

    /// <summary>
    /// Layouts and submits glyph quads directly into the target <see cref="SpriteBatch"/>.
    /// Zero heap allocation on hot path.
    /// </summary>
    /// <param name="spriteBatch">The sprite batch to submit quads to.</param>
    /// <param name="atlas">The font atlas containing glyph textures.</param>
    /// <param name="metrics">The font-wide metrics.</param>
    /// <param name="text">The text characters to layout.</param>
    /// <param name="options">Layout constraints and alignment.</param>
    /// <param name="position">Top-left anchor position.</param>
    /// <param name="color">Text tint color.</param>
    /// <param name="scale">Scale multiplier.</param>
    /// <param name="depth">Render depth buffer value.</param>
    /// <param name="footY">2.5D feet base Y coordinate for depth sorting.</param>
    /// <param name="layer">Render layer index.</param>
    public static void LayoutAndDraw(
        SpriteBatch spriteBatch,
        BitmapFontAtlas atlas,
        in FontMetrics metrics,
        ReadOnlySpan<char> text,
        in TextLayoutOptions options,
        Vector2 position,
        ColorRgba color,
        float scale = 1.0f,
        float depth = 0f,
        float footY = 0f,
        int layer = 0)
    {
        ArgumentNullException.ThrowIfNull(spriteBatch, nameof(spriteBatch));
        ArgumentNullException.ThrowIfNull(atlas, nameof(atlas));

        if (text.IsEmpty || scale <= 0f)
        {
            return;
        }

        Vector2 scaleVector = new(scale, scale);
        float scaledLineStep = (metrics.LineHeight + options.LineSpacing) * scale;
        float scaledSpace = metrics.SpaceWidth * scale;
        float scaledTab = metrics.TabWidth * scale;
        float scaledLetterSpacing = options.LetterSpacing * scale;
        float finalFootY = footY == 0f ? position.Y : footY;

        // If alignment is Center or Right and MaxWidth is not set, measure text width first
        float referenceWidth = options.MaxWidth;
        if ((options.Alignment == TextAlignment.Center || options.Alignment == TextAlignment.Right) && referenceWidth <= 0f)
        {
            TextMeasureResult measure = Measure(atlas, metrics, text, options, scale);
            referenceWidth = measure.Width;
        }

        int i = 0;
        int currentLineIndex = 0;

        while (i < text.Length)
        {
            // First pass for current line: find end of line and line width in screen pixels
            int lineScan = i;
            int lineStart = i;
            float lineWidth = 0f;
            int lastSpaceIndex = -1;
            float widthAtLastSpace = 0f;
            int lineEndIndex = text.Length;
            int nextLineStartIndex = text.Length;
            char prevScanChar = '\0';

            while (lineScan < text.Length)
            {
                char c = text[lineScan];

                if (c == '\r')
                {
                    lineEndIndex = lineScan;
                    nextLineStartIndex = (lineScan + 1 < text.Length && text[lineScan + 1] == '\n')
                        ? lineScan + 2
                        : lineScan + 1;
                    break;
                }

                if (c == '\n')
                {
                    lineEndIndex = lineScan;
                    nextLineStartIndex = lineScan + 1;
                    break;
                }

                if (c == '\t')
                {
                    lineWidth += scaledTab + scaledLetterSpacing;
                    prevScanChar = '\t';
                    lineScan++;
                    continue;
                }

                if (c == ' ')
                {
                    lastSpaceIndex = lineScan;
                    widthAtLastSpace = lineWidth;
                    lineWidth += scaledSpace + scaledLetterSpacing;
                    prevScanChar = ' ';
                    lineScan++;
                    continue;
                }

                float advance;
                if (atlas.TryGetGlyph(c, out FontGlyph scanGlyph))
                {
                    float kerning = prevScanChar != '\0' ? atlas.GetKerning(prevScanChar, c) : 0f;
                    advance = ((scanGlyph.XAdvance + kerning) * scale) + scaledLetterSpacing;
                }
                else
                {
                    advance = scaledSpace + scaledLetterSpacing;
                }

                if (options.WordWrap && options.MaxWidth > 0f && (lineWidth + advance) > options.MaxWidth)
                {
                    if (lastSpaceIndex >= lineStart)
                    {
                        lineEndIndex = lastSpaceIndex;
                        nextLineStartIndex = lastSpaceIndex + 1;
                        lineWidth = widthAtLastSpace;
                        break;
                    }

                    if (lineWidth > 0f)
                    {
                        lineEndIndex = lineScan;
                        nextLineStartIndex = lineScan;
                        break;
                    }
                }

                lineWidth += advance;
                prevScanChar = c;
                lineScan++;
            }

            if (lineScan >= text.Length && lineEndIndex == text.Length)
            {
                nextLineStartIndex = text.Length;
            }

            // Calculate alignment offset for this line
            float lineAlignOffset = 0f;
            if (options.Alignment == TextAlignment.Center)
            {
                lineAlignOffset = (referenceWidth - lineWidth) * 0.5f;
            }
            else if (options.Alignment == TextAlignment.Right)
            {
                lineAlignOffset = referenceWidth - lineWidth;
            }

            float currentLineY = position.Y + (currentLineIndex * scaledLineStep);
            float cursorX = lineAlignOffset;
            char prevChar = '\0';

            // Second pass: render glyphs on this line
            for (int charIdx = i; charIdx < lineEndIndex; charIdx++)
            {
                char c = text[charIdx];

                if (c == '\t')
                {
                    cursorX += scaledTab + scaledLetterSpacing;
                    prevChar = '\t';
                    continue;
                }

                if (c == ' ')
                {
                    cursorX += scaledSpace + scaledLetterSpacing;
                    prevChar = ' ';
                    continue;
                }

                if (atlas.TryGetGlyph(c, out FontGlyph glyph))
                {
                    float kerning = prevChar != '\0' ? atlas.GetKerning(prevChar, c) : 0f;
                    cursorX += kerning * scale;

                    Vector2 glyphPos = new(
                        position.X + cursorX + (glyph.XOffset * scale),
                        currentLineY + (glyph.YOffset * scale));

                    spriteBatch.Draw(
                        atlas.Texture,
                        glyphPos,
                        glyph.Bounds,
                        color,
                        rotation: 0f,
                        origin: Vector2.Zero,
                        scale: scaleVector,
                        effects: SpriteEffects.None,
                        depth: depth,
                        footY: finalFootY,
                        layer: layer);

                    cursorX += (glyph.XAdvance * scale) + scaledLetterSpacing;
                    prevChar = c;
                }
                else
                {
                    cursorX += scaledSpace + scaledLetterSpacing;
                    prevChar = c;
                }
            }

            currentLineIndex++;
            i = nextLineStartIndex;
        }
    }
}
