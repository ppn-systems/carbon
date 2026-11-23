// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Graphics.Fonts.Layout;

/// <summary>
/// Specifies layout and formatting constraints for measuring and rendering text blocks.
/// </summary>
public readonly struct TextLayoutOptions : IEquatable<TextLayoutOptions>
{
    /// <summary>
    /// Gets the maximum allowed line width in pixels. If zero or negative, wrapping is disabled.
    /// </summary>
    public float MaxWidth { get; }

    /// <summary>
    /// Gets a value indicating whether text should automatically break into new lines when exceeding <see cref="MaxWidth"/>.
    /// </summary>
    public bool WordWrap { get; }

    /// <summary>
    /// Gets the horizontal alignment of text lines (Left, Center, Right).
    /// </summary>
    public TextAlignment Alignment { get; }

    /// <summary>
    /// Gets additional vertical spacing in pixels added between consecutive lines.
    /// </summary>
    public float LineSpacing { get; }

    /// <summary>
    /// Gets additional horizontal spacing in pixels added between consecutive characters.
    /// </summary>
    public float LetterSpacing { get; }

    /// <summary>
    /// Default single-line left-aligned layout options with no extra spacing.
    /// </summary>
    public static readonly TextLayoutOptions Default;

    /// <summary>
    /// Initializes a new instance of the <see cref="TextLayoutOptions"/> struct.
    /// </summary>
    /// <param name="maxWidth">Maximum line width before wrapping.</param>
    /// <param name="wordWrap">Whether word wrapping is enabled.</param>
    /// <param name="alignment">Horizontal text alignment.</param>
    /// <param name="lineSpacing">Additional vertical line spacing.</param>
    /// <param name="letterSpacing">Additional horizontal character spacing.</param>
    public TextLayoutOptions(
        float maxWidth = 0f,
        bool wordWrap = false,
        TextAlignment alignment = TextAlignment.Left,
        float lineSpacing = 0f,
        float letterSpacing = 0f)
    {
        this.MaxWidth = maxWidth;
        this.WordWrap = wordWrap;
        this.Alignment = alignment;
        this.LineSpacing = lineSpacing;
        this.LetterSpacing = letterSpacing;
    }

    /// <inheritdoc/>
    public bool Equals(TextLayoutOptions other) =>
        Math.Abs(this.MaxWidth - other.MaxWidth) < 0.0001f
        && this.WordWrap == other.WordWrap
        && this.Alignment == other.Alignment
        && Math.Abs(this.LineSpacing - other.LineSpacing) < 0.0001f
        && Math.Abs(this.LetterSpacing - other.LetterSpacing) < 0.0001f;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is TextLayoutOptions other && this.Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(this.MaxWidth, this.WordWrap, this.Alignment, this.LineSpacing, this.LetterSpacing);

    /// <summary>
    /// Compares two <see cref="TextLayoutOptions"/> instances for equality.
    /// </summary>
    public static bool operator ==(TextLayoutOptions left, TextLayoutOptions right) => left.Equals(right);

    /// <summary>
    /// Compares two <see cref="TextLayoutOptions"/> instances for inequality.
    /// </summary>
    public static bool operator !=(TextLayoutOptions left, TextLayoutOptions right) => !left.Equals(right);
}
