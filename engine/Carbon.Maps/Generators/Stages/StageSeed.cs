// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Maps.Generators.Stages;

/// <summary>
/// Derives an independent seed for a stage from the world seed and the stage's name, so adding or
/// reordering stages never changes what the existing ones produce.
/// </summary>
public static class StageSeed
{
    /// <summary>Derives a stage seed.</summary>
    /// <param name="worldSeed">The world seed.</param>
    /// <param name="stageName">A stable, unique stage name (e.g. "caves").</param>
    /// <returns>The stage seed.</returns>
    public static ulong Derive(ulong worldSeed, string stageName)
    {
        ArgumentNullException.ThrowIfNull(stageName);
        ulong h = 14695981039346656037UL;
        foreach (char c in stageName)
        {
            h = (h ^ c) * 1099511628211UL;
        }

        // SplitMix64 finaliser over seed ^ name hash
        ulong z = worldSeed ^ h;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
