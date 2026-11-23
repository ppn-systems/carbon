// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Graphics.Batching.Common;

namespace Carbon.Graphics.Fonts.Model;

/// <summary>
/// Represents a single character glyph within a font atlas, defining its texture boundary and placement metrics.
/// </summary>
public readonly struct FontGlyph : IEquatable<FontGlyph>
{
    /// <summary>
    /// Gets the Unicode code point of this character.
    /// </summary>
    public int Character { get; }

    /// <summary>
    /// Gets the pixel rectangle on the font atlas texture containing this glyph.
    /// </summary>
    public RectangleF Bounds { get; }

    /// <summary>
    /// Gets the horizontal offset from the current pen position to the left edge of the glyph bitmap.
    /// </summary>
    public float XOffset { get; }

    /// <summary>
    /// Gets the vertical offset from the text baseline to the top edge of the glyph bitmap.
    /// </summary>
    public float YOffset { get; }

    /// <summary>
    /// Gets the horizontal distance to advance the cursor after rendering this glyph.
    /// </summary>
    public float XAdvance { get; }

    /// <summary>
    /// Gets the texture page index (for multi-texture font atlases). Typically 0.
    /// </summary>
    public int Page { get; }

    /// <summary>
    /// Gets the width of the glyph bounding box in pixels.
    /// </summary>
    public float Width => this.Bounds.Width;

    /// <summary>
    /// Gets the height of the glyph bounding box in pixels.
    /// </summary>
    public float Height => this.Bounds.Height;

    /// <summary>
    /// Gets a value indicating whether this glyph represents a valid character.
    /// </summary>
    public bool IsValid => this.Character > 0;

    /// <summary>
    /// Represents an empty or uninitialized glyph.
    /// </summary>
    public static readonly FontGlyph Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="FontGlyph"/> struct.
    /// </summary>
    /// <param name="character">The Unicode code point.</param>
    /// <param name="bounds">The pixel rectangle on the atlas texture.</param>
    /// <param name="xOffset">The horizontal bearing offset.</param>
    /// <param name="yOffset">The vertical bearing offset.</param>
    /// <param name="xAdvance">The horizontal advance distance.</param>
    /// <param name="page">The texture page index.</param>
    public FontGlyph(
        int character,
        in RectangleF bounds,
        float xOffset,
        float yOffset,
        float xAdvance,
        int page = 0)
    {
        this.Character = character;
        this.Bounds = bounds;
        this.XOffset = xOffset;
        this.YOffset = yOffset;
        this.XAdvance = xAdvance;
        this.Page = page;
    }

    /// <inheritdoc/>
    public bool Equals(FontGlyph other) =>
        this.Character == other.Character
        && this.Bounds.Equals(other.Bounds)
        && Math.Abs(this.XOffset - other.XOffset) < 0.0001f
        && Math.Abs(this.YOffset - other.YOffset) < 0.0001f
        && Math.Abs(this.XAdvance - other.XAdvance) < 0.0001f
        && this.Page == other.Page;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is FontGlyph other && this.Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(this.Character, this.Bounds, this.XOffset, this.YOffset, this.XAdvance, this.Page);

    /// <summary>
    /// Compares two <see cref="FontGlyph"/> instances for equality.
    /// </summary>
    public static bool operator ==(FontGlyph left, FontGlyph right) => left.Equals(right);

    /// <summary>
    /// Compares two <see cref="FontGlyph"/> instances for inequality.
    /// </summary>
    public static bool operator !=(FontGlyph left, FontGlyph right) => !left.Equals(right);
}
