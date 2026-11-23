// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Graphics.Fonts.Model;

/// <summary>
/// Represents a kerning adjustment between two consecutive characters.
/// </summary>
public readonly struct KerningPair : IEquatable<KerningPair>
{
    /// <summary>
    /// Gets the first character in the pair.
    /// </summary>
    public char First { get; }

    /// <summary>
    /// Gets the second character in the pair.
    /// </summary>
    public char Second { get; }

    /// <summary>
    /// Gets the horizontal offset amount in pixels applied between these two characters.
    /// </summary>
    public float Amount { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="KerningPair"/> struct.
    /// </summary>
    /// <param name="first">The first character.</param>
    /// <param name="second">The second character.</param>
    /// <param name="amount">The horizontal adjustment amount.</param>
    public KerningPair(char first, char second, float amount)
    {
        this.First = first;
        this.Second = second;
        this.Amount = amount;
    }

    /// <inheritdoc/>
    public bool Equals(KerningPair other) =>
        this.First == other.First
        && this.Second == other.Second
        && Math.Abs(this.Amount - other.Amount) < 0.0001f;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is KerningPair other && this.Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(this.First, this.Second, this.Amount);

    /// <summary>
    /// Compares two <see cref="KerningPair"/> instances for equality.
    /// </summary>
    public static bool operator ==(KerningPair left, KerningPair right) => left.Equals(right);

    /// <summary>
    /// Compares two <see cref="KerningPair"/> instances for inequality.
    /// </summary>
    public static bool operator !=(KerningPair left, KerningPair right) => !left.Equals(right);
}
