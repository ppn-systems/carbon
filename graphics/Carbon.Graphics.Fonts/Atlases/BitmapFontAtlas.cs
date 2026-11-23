// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Graphics.Fonts.Model;
using Carbon.Graphics.OpenGL.Abstractions;

namespace Carbon.Graphics.Fonts.Atlases;

/// <summary>
/// Provides high-throughput glyph lookups via a direct ASCII O(1) table combined with a sparse dictionary for extended Unicode.
/// Zero heap allocation on character query hot paths.
/// </summary>
public sealed class BitmapFontAtlas : IFontAtlas
{
    private readonly FontGlyph[] _asciiTable = new FontGlyph[128];
    private readonly Dictionary<int, FontGlyph> _unicodeGlyphs = [];
    private readonly Dictionary<long, float> _kerningMap = [];
    private int _glyphCount;

    /// <inheritdoc/>
    public ITexture Texture { get; }

    /// <summary>
    /// Gets the total number of glyphs defined in this atlas.
    /// </summary>
    public int GlyphCount => _glyphCount;

    /// <summary>
    /// Gets or sets the fallback glyph returned when an unmapped character is queried (e.g. '?' or box).
    /// </summary>
    public FontGlyph FallbackGlyph { get; set; } = FontGlyph.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="BitmapFontAtlas"/> class.
    /// </summary>
    /// <param name="texture">The font atlas texture.</param>
    public BitmapFontAtlas(ITexture texture) =>
        this.Texture = texture ?? throw new ArgumentNullException(nameof(texture));

    /// <summary>
    /// Registers a glyph in the atlas. Uses dense array for ASCII code points (0-127) and sparse map for others.
    /// </summary>
    /// <param name="glyph">The glyph definition to store.</param>
    public void AddGlyph(in FontGlyph glyph)
    {
        int code = glyph.Character;
        if (code >= 0 && code < 128)
        {
            if (!_asciiTable[code].IsValid)
            {
                _glyphCount++;
            }

            _asciiTable[code] = glyph;
        }
        else
        {
            if (!_unicodeGlyphs.ContainsKey(code))
            {
                _glyphCount++;
            }

            _unicodeGlyphs[code] = glyph;
        }
    }

    /// <summary>
    /// Registers a kerning adjustment between two characters.
    /// </summary>
    /// <param name="first">The first character.</param>
    /// <param name="second">The second character.</param>
    /// <param name="amount">The kerning offset in pixels.</param>
    public void AddKerning(char first, char second, float amount)
    {
        long key = ((long)first << 32) | second;
        _kerningMap[key] = amount;
    }

    /// <inheritdoc/>
    public bool TryGetGlyph(char character, out FontGlyph glyph)
    {
        if (character < 128)
        {
            glyph = _asciiTable[character];
            if (glyph.IsValid)
            {
                return true;
            }
        }
        else if (_unicodeGlyphs.TryGetValue(character, out glyph))
        {
            return true;
        }

        if (this.FallbackGlyph.IsValid)
        {
            glyph = this.FallbackGlyph;
            return true;
        }

        glyph = FontGlyph.Empty;
        return false;
    }

    /// <inheritdoc/>
    public bool TryGetGlyph(int codePoint, out FontGlyph glyph)
    {
        if (codePoint >= 0 && codePoint < 128)
        {
            glyph = _asciiTable[codePoint];
            if (glyph.IsValid)
            {
                return true;
            }
        }
        else if (_unicodeGlyphs.TryGetValue(codePoint, out glyph))
        {
            return true;
        }

        if (this.FallbackGlyph.IsValid)
        {
            glyph = this.FallbackGlyph;
            return true;
        }

        glyph = FontGlyph.Empty;
        return false;
    }

    /// <inheritdoc/>
    public float GetKerning(char first, char second)
    {
        if (_kerningMap.Count == 0)
        {
            return 0f;
        }

        long key = ((long)first << 32) | second;
        return _kerningMap.TryGetValue(key, out float amount) ? amount : 0f;
    }
}
