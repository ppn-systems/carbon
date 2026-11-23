// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Graphics.UI.NineSlice;

/// <summary>
/// Defines the border margins in pixels for nine-slice panel slicing and rendering.
/// </summary>
public readonly struct NineSliceMargins : IEquatable<NineSliceMargins>
{
    /// <summary>
    /// Gets the width of the left border margin in pixels.
    /// </summary>
    public float Left { get; }

    /// <summary>
    /// Gets the height of the top border margin in pixels.
    /// </summary>
    public float Top { get; }

    /// <summary>
    /// Gets the width of the right border margin in pixels.
    /// </summary>
    public float Right { get; }

    /// <summary>
    /// Gets the height of the bottom border margin in pixels.
    /// </summary>
    public float Bottom { get; }

    /// <summary>
    /// Gets the total horizontal margins (Left + Right).
    /// </summary>
    public float Horizontal => this.Left + this.Right;

    /// <summary>
    /// Gets the total vertical margins (Top + Bottom).
    /// </summary>
    public float Vertical => this.Top + this.Bottom;

    /// <summary>
    /// Initializes a new instance of the <see cref="NineSliceMargins"/> struct with a uniform margin on all 4 sides.
    /// </summary>
    /// <param name="all">Margin in pixels applied to all four sides.</param>
    public NineSliceMargins(float all)
    {
        this.Left = all;
        this.Top = all;
        this.Right = all;
        this.Bottom = all;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NineSliceMargins"/> struct with individual side distances.
    /// </summary>
    /// <param name="left">Left margin in pixels.</param>
    /// <param name="top">Top margin in pixels.</param>
    /// <param name="right">Right margin in pixels.</param>
    /// <param name="bottom">Bottom margin in pixels.</param>
    public NineSliceMargins(float left, float top, float right, float bottom)
    {
        this.Left = left;
        this.Top = top;
        this.Right = right;
        this.Bottom = bottom;
    }

    /// <inheritdoc/>
    public bool Equals(NineSliceMargins other) =>
        Math.Abs(this.Left - other.Left) < 0.0001f
        && Math.Abs(this.Top - other.Top) < 0.0001f
        && Math.Abs(this.Right - other.Right) < 0.0001f
        && Math.Abs(this.Bottom - other.Bottom) < 0.0001f;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is NineSliceMargins other && this.Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(this.Left, this.Top, this.Right, this.Bottom);

    /// <summary>
    /// Compares two <see cref="NineSliceMargins"/> instances for equality.
    /// </summary>
    public static bool operator ==(NineSliceMargins left, NineSliceMargins right) => left.Equals(right);

    /// <summary>
    /// Compares two <see cref="NineSliceMargins"/> instances for inequality.
    /// </summary>
    public static bool operator !=(NineSliceMargins left, NineSliceMargins right) => !left.Equals(right);
}
