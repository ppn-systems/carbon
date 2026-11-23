// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Graphics.UI.Common;

/// <summary>
/// Represents inside or outside padding spacing around the edges of a UI element.
/// </summary>
public readonly struct Padding : IEquatable<Padding>
{
    /// <summary>
    /// Gets the left padding distance in pixels.
    /// </summary>
    public float Left { get; }

    /// <summary>
    /// Gets the top padding distance in pixels.
    /// </summary>
    public float Top { get; }

    /// <summary>
    /// Gets the right padding distance in pixels.
    /// </summary>
    public float Right { get; }

    /// <summary>
    /// Gets the bottom padding distance in pixels.
    /// </summary>
    public float Bottom { get; }

    /// <summary>
    /// Gets the combined horizontal padding (Left + Right).
    /// </summary>
    public float Horizontal => this.Left + this.Right;

    /// <summary>
    /// Gets the combined vertical padding (Top + Bottom).
    /// </summary>
    public float Vertical => this.Top + this.Bottom;

    /// <summary>
    /// Represents empty zero padding.
    /// </summary>
    public static readonly Padding Zero;

    /// <summary>
    /// Initializes a new instance of the <see cref="Padding"/> struct with uniform padding on all sides.
    /// </summary>
    /// <param name="all">Padding in pixels applied to all four sides.</param>
    public Padding(float all)
    {
        this.Left = all;
        this.Top = all;
        this.Right = all;
        this.Bottom = all;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Padding"/> struct with symmetric horizontal and vertical values.
    /// </summary>
    /// <param name="horizontal">Left and right padding.</param>
    /// <param name="vertical">Top and bottom padding.</param>
    public Padding(float horizontal, float vertical)
    {
        this.Left = horizontal;
        this.Top = vertical;
        this.Right = horizontal;
        this.Bottom = vertical;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Padding"/> struct with individual side distances.
    /// </summary>
    /// <param name="left">Left padding.</param>
    /// <param name="top">Top padding.</param>
    /// <param name="right">Right padding.</param>
    /// <param name="bottom">Bottom padding.</param>
    public Padding(float left, float top, float right, float bottom)
    {
        this.Left = left;
        this.Top = top;
        this.Right = right;
        this.Bottom = bottom;
    }

    /// <inheritdoc/>
    public bool Equals(Padding other) =>
        Math.Abs(this.Left - other.Left) < 0.0001f
        && Math.Abs(this.Top - other.Top) < 0.0001f
        && Math.Abs(this.Right - other.Right) < 0.0001f
        && Math.Abs(this.Bottom - other.Bottom) < 0.0001f;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Padding other && this.Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(this.Left, this.Top, this.Right, this.Bottom);

    /// <summary>
    /// Compares two <see cref="Padding"/> instances for equality.
    /// </summary>
    public static bool operator ==(Padding left, Padding right) => left.Equals(right);

    /// <summary>
    /// Compares two <see cref="Padding"/> instances for inequality.
    /// </summary>
    public static bool operator !=(Padding left, Padding right) => !left.Equals(right);
}
