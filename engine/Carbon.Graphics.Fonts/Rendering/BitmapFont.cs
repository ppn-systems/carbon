// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Fonts.Atlases;
using Carbon.Graphics.Fonts.Layout;
using Carbon.Graphics.Fonts.Model;
using Carbon.Graphics.OpenGL.Abstractions;

namespace Carbon.Graphics.Fonts.Rendering;

/// <summary>
/// Represents a high-performance bitmap or signed-distance-field (SDF) font asset ready for rendering.
/// Encapsulates texture atlas, character metrics, kerning pairs, and loaders.
/// </summary>
public sealed class BitmapFont
{
    /// <summary>
    /// Gets the friendly identifier or family name of this font.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the primary texture atlas containing all glyph bitmaps for this font.
    /// </summary>
    public ITexture Texture => this.Atlas.Texture;

    /// <summary>
    /// Gets the glyph lookup atlas.
    /// </summary>
    public BitmapFontAtlas Atlas { get; }

    /// <summary>
    /// Gets font-wide metric dimensions (line height, baseline, font size).
    /// </summary>
    public FontMetrics Metrics { get; }

    /// <summary>
    /// Gets a value indicating whether this font uses Signed Distance Field (SDF) encoding for resolution-independent scaling.
    /// </summary>
    public bool IsDistanceField { get; }

    /// <summary>
    /// Gets the vertical line height in pixels.
    /// </summary>
    public float LineHeight => this.Metrics.LineHeight;

    /// <summary>
    /// Gets the design font size in points or pixels.
    /// </summary>
    public float FontSize => this.Metrics.FontSize;

    /// <summary>
    /// Initializes a new instance of the <see cref="BitmapFont"/> class.
    /// </summary>
    /// <param name="name">The font name.</param>
    /// <param name="atlas">The font atlas.</param>
    /// <param name="metrics">The font metrics.</param>
    /// <param name="isDistanceField">Whether this font is an SDF font.</param>
    public BitmapFont(string name, BitmapFontAtlas atlas, in FontMetrics metrics, bool isDistanceField = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(name, nameof(name));
        this.Name = name;
        this.Atlas = atlas ?? throw new ArgumentNullException(nameof(atlas));
        this.Metrics = metrics;
        this.IsDistanceField = isDistanceField;
    }

    /// <summary>
    /// Measures the dimensions of the given text when rendered with this font.
    /// Zero heap allocation.
    /// </summary>
    /// <param name="text">The text characters to measure.</param>
    /// <param name="options">Layout constraints and spacing options.</param>
    /// <param name="scale">Scale multiplier (default 1.0).</param>
    /// <returns>The measured bounds and line count.</returns>
    public TextMeasureResult MeasureString(ReadOnlySpan<char> text, in TextLayoutOptions options, float scale = 1.0f) =>
        TextLayoutEngine.Measure(this.Atlas, this.Metrics, text, options, scale);

    /// <summary>
    /// Measures the dimensions of the given text with optional max width and word wrapping.
    /// </summary>
    /// <param name="text">The text characters to measure.</param>
    /// <param name="maxWidth">Maximum line width before wrapping.</param>
    /// <param name="wordWrap">Whether word wrapping is enabled.</param>
    /// <param name="scale">Scale multiplier (default 1.0).</param>
    /// <returns>The measured bounds and line count.</returns>
    public TextMeasureResult MeasureString(ReadOnlySpan<char> text, float maxWidth = 0f, bool wordWrap = false, float scale = 1.0f) =>
        TextLayoutEngine.Measure(this.Atlas, this.Metrics, text, new TextLayoutOptions(maxWidth, wordWrap), scale);

    /// <summary>
    /// Parses an AngelCode BMFont text descriptor format (.fnt) and constructs a complete <see cref="BitmapFont"/>.
    /// Supported by BMFont, Hiero, ShoeBox, Glyph Designer, and modern font exporters.
    /// </summary>
    /// <param name="name">The font name.</param>
    /// <param name="texture">The underlying font texture atlas.</param>
    /// <param name="fntContent">The text content of the .fnt descriptor file.</param>
    /// <param name="isDistanceField">Whether the atlas texture is encoded as SDF.</param>
    /// <returns>A new populated <see cref="BitmapFont"/> instance.</returns>
    public static BitmapFont CreateFromBmFont(
        string name,
        ITexture texture,
        string fntContent,
        bool isDistanceField = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(name, nameof(name));
        ArgumentNullException.ThrowIfNull(texture, nameof(texture));
        ArgumentNullException.ThrowIfNull(fntContent, nameof(fntContent));

        BitmapFontAtlas atlas = new(texture);

        float fontSize = 16f;
        float lineHeight = 20f;
        float baseLine = 16f;

        ReadOnlySpan<char> span = fntContent.AsSpan();

        while (!span.IsEmpty)
        {
            int lineEnd = span.IndexOf('\n');
            ReadOnlySpan<char> line = lineEnd >= 0 ? span[..lineEnd] : span;
            span = lineEnd >= 0 ? span[(lineEnd + 1)..] : [];

            line = line.Trim();
            if (line.IsEmpty)
            {
                continue;
            }

            if (line.StartsWith("info ", StringComparison.Ordinal))
            {
                if (TryGetValue(line, "size", out float parsedSize))
                {
                    fontSize = Math.Abs(parsedSize);
                }
            }
            else if (line.StartsWith("common ", StringComparison.Ordinal))
            {
                if (TryGetValue(line, "lineHeight", out float parsedLineHeight))
                {
                    lineHeight = parsedLineHeight;
                }

                if (TryGetValue(line, "base", out float parsedBase))
                {
                    baseLine = parsedBase;
                }
            }
            else if (line.StartsWith("char ", StringComparison.Ordinal))
            {
                if (TryGetValue(line, "id", out int id)
                    && TryGetValue(line, "x", out float x)
                    && TryGetValue(line, "y", out float y)
                    && TryGetValue(line, "width", out float width)
                    && TryGetValue(line, "height", out float height)
                    && TryGetValue(line, "xoffset", out float xoffset)
                    && TryGetValue(line, "yoffset", out float yoffset)
                    && TryGetValue(line, "xadvance", out float xadvance))
                {
                    _ = TryGetValue(line, "page", out int page);

                    RectangleF bounds = new(x, y, width, height);
                    FontGlyph glyph = new(id, bounds, xoffset, yoffset, xadvance, page);
                    atlas.AddGlyph(glyph);
                }
            }
            else if (line.StartsWith("kerning ", StringComparison.Ordinal))
            {
                if (TryGetValue(line, "first", out int first)
                    && TryGetValue(line, "second", out int second)
                    && TryGetValue(line, "amount", out float amount))
                {
                    atlas.AddKerning((char)first, (char)second, amount);
                }
            }
        }

        FontMetrics metrics = new(fontSize, lineHeight, baseLine);
        return new BitmapFont(name, atlas, metrics, isDistanceField);
    }

    /// <summary>
    /// Creates a retro monospace pixel font from a regular grid spritesheet.
    /// </summary>
    /// <param name="name">The font name.</param>
    /// <param name="texture">The grid texture sheet.</param>
    /// <param name="cellWidth">Pixel width of each character cell.</param>
    /// <param name="cellHeight">Pixel height of each character cell.</param>
    /// <param name="fontSize">Effective font size.</param>
    /// <param name="characterSet">Sequence of characters ordered by grid layout.</param>
    /// <returns>A new populated <see cref="BitmapFont"/> instance.</returns>
    public static BitmapFont CreateMonospaceGrid(
        string name,
        ITexture texture,
        int cellWidth,
        int cellHeight,
        float fontSize,
        string characterSet)
    {
        ArgumentException.ThrowIfNullOrEmpty(name, nameof(name));
        ArgumentNullException.ThrowIfNull(texture, nameof(texture));
        ArgumentException.ThrowIfNullOrEmpty(characterSet, nameof(characterSet));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellWidth, nameof(cellWidth));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellHeight, nameof(cellHeight));

        BitmapFontAtlas atlas = new(texture);
        int cols = (int)texture.Width / cellWidth;

        for (int i = 0; i < characterSet.Length; i++)
        {
            char c = characterSet[i];
            int col = i % cols;
            int row = i / cols;

            RectangleF bounds = new(col * cellWidth, row * cellHeight, cellWidth, cellHeight);
            FontGlyph glyph = new(c, bounds, 0f, 0f, cellWidth);
            atlas.AddGlyph(glyph);
        }

        FontMetrics metrics = new(fontSize, cellHeight, cellHeight);
        return new BitmapFont(name, atlas, metrics, isDistanceField: false);
    }

    private static bool TryGetValue(ReadOnlySpan<char> line, string key, out float value)
    {
        value = 0f;
        int keyIndex = line.IndexOf(key.AsSpan(), StringComparison.Ordinal);
        if (keyIndex < 0)
        {
            return false;
        }

        int eqIndex = keyIndex + key.Length;
        if (eqIndex >= line.Length || line[eqIndex] != '=')
        {
            return false;
        }

        ReadOnlySpan<char> rest = line[(eqIndex + 1)..];
        int spaceIndex = rest.IndexOf(' ');
        ReadOnlySpan<char> valSpan = spaceIndex >= 0 ? rest[..spaceIndex] : rest;

        return float.TryParse(valSpan, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetValue(ReadOnlySpan<char> line, string key, out int value)
    {
        value = 0;
        int keyIndex = line.IndexOf(key.AsSpan(), StringComparison.Ordinal);
        if (keyIndex < 0)
        {
            return false;
        }

        int eqIndex = keyIndex + key.Length;
        if (eqIndex >= line.Length || line[eqIndex] != '=')
        {
            return false;
        }

        ReadOnlySpan<char> rest = line[(eqIndex + 1)..];
        int spaceIndex = rest.IndexOf(' ');
        ReadOnlySpan<char> valSpan = spaceIndex >= 0 ? rest[..spaceIndex] : rest;

        return int.TryParse(valSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
