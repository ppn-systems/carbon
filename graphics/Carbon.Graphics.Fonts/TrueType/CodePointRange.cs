// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Graphics.Fonts.TrueType;

/// <summary>
/// Inclusive range of Unicode code points to bake into a font atlas.
/// </summary>
public readonly record struct CodePointRange
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CodePointRange"/> struct.
    /// </summary>
    /// <param name="first">First code point (inclusive).</param>
    /// <param name="last">Last code point (inclusive).</param>
    public CodePointRange(int first, int last)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(first, nameof(first));
        ArgumentOutOfRangeException.ThrowIfLessThan(last, first, nameof(last));
        this.First = first;
        this.Last = last;
    }

    /// <summary>Gets the first code point (inclusive).</summary>
    public int First { get; }

    /// <summary>Gets the last code point (inclusive).</summary>
    public int Last { get; }

    /// <summary>Returns whether <paramref name="codePoint"/> lies inside this range.</summary>
    /// <param name="codePoint">The code point to test.</param>
    /// <returns><see langword="true"/> when inside the range.</returns>
    public bool Contains(int codePoint) => codePoint >= this.First && codePoint <= this.Last;
}
