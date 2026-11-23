// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Graphics.Fonts.Model;
using Carbon.Graphics.OpenGL.Abstractions;

namespace Carbon.Graphics.Fonts.Atlases;

/// <summary>
/// Defines the contract for retrieving character glyphs and kerning data from a font atlas.
/// </summary>
public interface IFontAtlas
{
    /// <summary>
    /// Gets the primary underlying texture sheet for this font atlas.
    /// </summary>
    ITexture Texture { get; }

    /// <summary>
    /// Attempts to retrieve the glyph for the specified character.
    /// </summary>
    /// <param name="character">The character to look up.</param>
    /// <param name="glyph">The output glyph structure if found; otherwise, empty glyph.</param>
    /// <returns><see langword="true"/> if the glyph exists; otherwise, <see langword="false"/>.</returns>
    bool TryGetGlyph(char character, out FontGlyph glyph);

    /// <summary>
    /// Attempts to retrieve the glyph for the specified Unicode code point.
    /// </summary>
    /// <param name="codePoint">The 32-bit Unicode code point.</param>
    /// <param name="glyph">The output glyph structure if found; otherwise, empty glyph.</param>
    /// <returns><see langword="true"/> if the glyph exists; otherwise, <see langword="false"/>.</returns>
    bool TryGetGlyph(int codePoint, out FontGlyph glyph);

    /// <summary>
    /// Retrieves the kerning spacing offset between two consecutive characters.
    /// </summary>
    /// <param name="first">The first character.</param>
    /// <param name="second">The second character.</param>
    /// <returns>The kerning offset in pixels, or 0.0 if no pair entry exists.</returns>
    float GetKerning(char first, char second);
}
