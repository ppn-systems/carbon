// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Graphics.Fonts.Model;

/// <summary>
/// Defines font-wide measurement metrics including line height, baseline distance, and default character spacing.
/// </summary>
public readonly struct FontMetrics : IEquatable<FontMetrics>
{
    /// <summary>
    /// Gets the original design point or pixel size of the font.
    /// </summary>
    public float FontSize { get; }

    /// <summary>
    /// Gets the vertical distance in pixels between the baselines of consecutive lines of text.
    /// </summary>
    public float LineHeight { get; }

    /// <summary>
    /// Gets the vertical distance from the top of the line box to the baseline in pixels.
    /// </summary>
    public float BaseLine { get; }

    /// <summary>
    /// Gets the horizontal advance width of the space character (' ') in pixels.
    /// </summary>
    public float SpaceWidth { get; }

    /// <summary>
    /// Gets the horizontal advance width of the tab character ('\t') in pixels.
    /// </summary>
    public float TabWidth { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="FontMetrics"/> struct.
    /// </summary>
    /// <param name="fontSize">The design font size.</param>
    /// <param name="lineHeight">The distance between lines.</param>
    /// <param name="baseLine">The baseline distance from top.</param>
    /// <param name="spaceWidth">Optional advance width for space. Defaults to fontSize * 0.33f if null.</param>
    /// <param name="tabWidth">Optional advance width for tab. Defaults to 4 * spaceWidth if null.</param>
    public FontMetrics(
        float fontSize,
        float lineHeight,
        float baseLine,
        float? spaceWidth = null,
        float? tabWidth = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fontSize, nameof(fontSize));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lineHeight, nameof(lineHeight));

        this.FontSize = fontSize;
        this.LineHeight = lineHeight;
        this.BaseLine = baseLine;

        float resolvedSpace = spaceWidth ?? (fontSize * 0.33f);
        this.SpaceWidth = resolvedSpace;
        this.TabWidth = tabWidth ?? (resolvedSpace * 4f);
    }

    /// <inheritdoc/>
    public bool Equals(FontMetrics other) =>
        Math.Abs(this.FontSize - other.FontSize) < 0.0001f
        && Math.Abs(this.LineHeight - other.LineHeight) < 0.0001f
        && Math.Abs(this.BaseLine - other.BaseLine) < 0.0001f
        && Math.Abs(this.SpaceWidth - other.SpaceWidth) < 0.0001f
        && Math.Abs(this.TabWidth - other.TabWidth) < 0.0001f;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is FontMetrics other && this.Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(this.FontSize, this.LineHeight, this.BaseLine, this.SpaceWidth, this.TabWidth);

    /// <summary>
    /// Compares two <see cref="FontMetrics"/> instances for equality.
    /// </summary>
    public static bool operator ==(FontMetrics left, FontMetrics right) => left.Equals(right);

    /// <summary>
    /// Compares two <see cref="FontMetrics"/> instances for inequality.
    /// </summary>
    public static bool operator !=(FontMetrics left, FontMetrics right) => !left.Equals(right);
}
