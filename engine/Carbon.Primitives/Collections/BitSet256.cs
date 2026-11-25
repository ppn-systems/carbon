// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;

namespace Carbon.Primitives.Collections;

/// <summary>
/// Inline 256-bit fixed bitmask stored completely by value with zero heap allocation.
/// </summary>
public record struct BitSet256
{
    private ulong _w0;
    private ulong _w1;
    private ulong _w2;
    private ulong _w3;

    /// <summary>
    /// Gets the total number of set bits in this bitset.
    /// </summary>
    public readonly int PopCount =>
        BitOperations.PopCount(_w0) +
        BitOperations.PopCount(_w1) +
        BitOperations.PopCount(_w2) +
        BitOperations.PopCount(_w3);

    /// <summary>
    /// Gets a value indicating whether zero bits are currently set.
    /// </summary>
    public readonly bool IsEmpty => (_w0 | _w1 | _w2 | _w3) == 0UL;

    /// <summary>
    /// Sets the bit at <paramref name="bitIndex"/> (0..255).
    /// </summary>
    /// <param name="bitIndex">Zero-based bit position.</param>
    public void Set(int bitIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bitIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bitIndex, 256);

        ulong mask = 1UL << (bitIndex & 63);
        switch (bitIndex >> 6)
        {
            case 0:
                _w0 |= mask;
                break;
            case 1:
                _w1 |= mask;
                break;
            case 2:
                _w2 |= mask;
                break;
            default:
                _w3 |= mask;
                break;
        }
    }

    /// <summary>
    /// Clears the bit at <paramref name="bitIndex"/> (0..255).
    /// </summary>
    /// <param name="bitIndex">Zero-based bit position.</param>
    public void Clear(int bitIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bitIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bitIndex, 256);

        ulong mask = ~(1UL << (bitIndex & 63));
        switch (bitIndex >> 6)
        {
            case 0:
                _w0 &= mask;
                break;
            case 1:
                _w1 &= mask;
                break;
            case 2:
                _w2 &= mask;
                break;
            default:
                _w3 &= mask;
                break;
        }
    }

    /// <summary>
    /// Determines whether the bit at <paramref name="bitIndex"/> (0..255) is set.
    /// </summary>
    /// <param name="bitIndex">Zero-based bit position.</param>
    /// <returns><see langword="true"/> if set; otherwise, <see langword="false"/>.</returns>
    public readonly bool IsSet(int bitIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bitIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bitIndex, 256);

        ulong word = (bitIndex >> 6) switch
        {
            0 => _w0,
            1 => _w1,
            2 => _w2,
            _ => _w3
        };
        return (word & (1UL << (bitIndex & 63))) != 0UL;
    }

    /// <summary>
    /// Clears every bit in this bitset to zero.
    /// </summary>
    public void ClearAll()
    {
        _w0 = 0UL;
        _w1 = 0UL;
        _w2 = 0UL;
        _w3 = 0UL;
    }
}
