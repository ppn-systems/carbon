// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;

namespace Carbon.Graphics.Fonts.Layout;

/// <summary>
/// Contains the geometric dimensions and line statistics resulting from measuring a text span.
/// </summary>
public readonly struct TextMeasureResult : IEquatable<TextMeasureResult>
{
    /// <summary>
    /// Gets the maximum line width of the measured text in pixels.
    /// </summary>
    public float Width { get; }

    /// <summary>
    /// Gets the total vertical height of the measured text block in pixels.
    /// </summary>
    public float Height { get; }

    /// <summary>
    /// Gets the total number of lines contained in the measured text.
    /// </summary>
    public int LineCount { get; }

    /// <summary>
    /// Gets the size of the text bounding box as a 2D vector (Width, Height).
    /// </summary>
    public Vector2 Size => new(this.Width, this.Height);

    /// <summary>
    /// Initializes a new instance of the <see cref="TextMeasureResult"/> struct.
    /// </summary>
    /// <param name="width">The measured maximum width.</param>
    /// <param name="height">The measured total height.</param>
    /// <param name="lineCount">The number of lines.</param>
    public TextMeasureResult(float width, float height, int lineCount)
    {
        this.Width = width;
        this.Height = height;
        this.LineCount = lineCount;
    }

    /// <inheritdoc/>
    public bool Equals(TextMeasureResult other) =>
        Math.Abs(this.Width - other.Width) < 0.0001f
        && Math.Abs(this.Height - other.Height) < 0.0001f
        && this.LineCount == other.LineCount;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is TextMeasureResult other && this.Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(this.Width, this.Height, this.LineCount);

    /// <summary>
    /// Compares two <see cref="TextMeasureResult"/> instances for equality.
    /// </summary>
    public static bool operator ==(TextMeasureResult left, TextMeasureResult right) => left.Equals(right);

    /// <summary>
    /// Compares two <see cref="TextMeasureResult"/> instances for inequality.
    /// </summary>
    public static bool operator !=(TextMeasureResult left, TextMeasureResult right) => !left.Equals(right);
}
