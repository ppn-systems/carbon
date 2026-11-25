// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Primitives.Hashing;

/// <summary>
/// Well-mixed, platform-independent integer hash for per-tile deterministic decisions.
/// Replaces ad-hoc <c>x * prime ^ y * prime</c> hashes, which produce visible lattice patterns.
/// </summary>
public static class SpatialHash
{
    /// <summary>Hashes a world tile coordinate together with a world seed (SplitMix64 finalizer).</summary>
    /// <param name="seed">World seed.</param>
    /// <param name="x">World tile X.</param>
    /// <param name="y">World tile Y.</param>
    /// <returns>A 64-bit avalanche-mixed hash.</returns>
    public static ulong Hash(ulong seed, int x, int y)
    {
        ulong z = seed ^ ((uint)x * 0x9E3779B97F4A7C15UL) ^ ((uint)y * 0xC2B2AE3D27D4EB4FUL);
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Hashes a world tile coordinate with a seed and a salt, returning 32 bits.</summary>
    /// <param name="seed">World seed.</param>
    /// <param name="x">World tile X.</param>
    /// <param name="y">World tile Y.</param>
    /// <param name="salt">Per-feature salt so independent decisions do not correlate.</param>
    /// <returns>A 32-bit hash.</returns>
    public static uint Hash32(ulong seed, int x, int y, ulong salt) =>
        (uint)(Hash(seed ^ salt, x, y) >> 32);
}
