// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Primitives.Math;

namespace Carbon.Primitives.Random;

/// <summary>
/// High-throughput, zero-allocation deterministic pseudo-random number generator based on Xoshiro256**.
/// State is initialized deterministically from a 64-bit seed via SplitMix64.
/// </summary>
public struct DeterministicRandom
{
    private const float TwoPi = 6.2831853071795864769f;

    private ulong _s0;
    private ulong _s1;
    private ulong _s2;
    private ulong _s3;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeterministicRandom"/> struct from a 64-bit seed.
    /// </summary>
    /// <param name="seed">Initial 64-bit integer seed.</param>
    public DeterministicRandom(ulong seed)
    {
        ulong state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;

        _s0 = SplitMix64(ref state);
        _s1 = SplitMix64(ref state);
        _s2 = SplitMix64(ref state);
        _s3 = SplitMix64(ref state);

        if ((_s0 | _s1 | _s2 | _s3) == 0)
        {
            _s0 = 0x9E3779B97F4A7C15UL;
        }
    }

    /// <summary>
    /// Generates the next pseudo-random 64-bit unsigned integer.
    /// </summary>
    public ulong NextULong()
    {
        ulong result = BitOperations.RotateLeft(_s1 * 5, 7) * 9;
        ulong t = _s1 << 17;

        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;

        _s2 ^= t;
        _s3 = BitOperations.RotateLeft(_s3, 45);

        return result;
    }

    /// <summary>
    /// Generates a floating point value in the range [0.0, 1.0).
    /// </summary>
    public float NextFloat() =>
        (this.NextULong() >> 40) * (1.0f / 16777216.0f);

    /// <summary>
    /// Generates a floating point value in the range [min, max).
    /// </summary>
    public float NextFloat(float min, float max) =>
        min + (this.NextFloat() * (max - min));

    /// <summary>
    /// Generates an integer in the range [minInclusive, maxExclusive).
    /// </summary>
    public int NextInt(int minInclusive, int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(minInclusive, maxExclusive, nameof(minInclusive));
        uint range = (uint)(maxExclusive - minInclusive);
        return minInclusive + (int)(this.NextULong() % range);
    }

    /// <summary>
    /// Generates a deterministic boolean that is <see langword="true"/> with the specified probability.
    /// </summary>
    /// <param name="probability">Probability in [0.0, 1.0].</param>
    /// <returns>Deterministic boolean outcome.</returns>
    public bool NextBool(float probability = 0.5f) =>
        this.NextFloat() < probability;

    /// <summary>
    /// Generates a deterministic 2D unit direction vector using <see cref="DeterministicMath"/>.
    /// </summary>
    /// <returns>Unit <see cref="Vector2"/>.</returns>
    public Vector2 NextUnitVector2()
    {
        float angle = this.NextFloat() * TwoPi;
        return new Vector2(DeterministicMath.Cos(angle), DeterministicMath.Sin(angle));
    }

    private static ulong SplitMix64(ref ulong state)
    {
        ulong z = state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
