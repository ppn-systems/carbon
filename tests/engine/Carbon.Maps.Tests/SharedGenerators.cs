// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Concurrent;
using Carbon.Maps.Generators;

namespace Carbon.Maps.Tests;

/// <summary>
/// One generator per seed for tests that only read generated content (ADR-045 D7). A generator caches its own world's
/// river regions, so tests that build a new one per case rebuild the same regions again and again. Tests of determinism
/// across generators, or of the caches themselves, keep constructing their own.
/// </summary>
internal static class SharedGenerators
{
    private static readonly ConcurrentDictionary<ulong, ProceduralWorldGenerator> s_bySeed = new();

    /// <summary>Gets the shared generator for <paramref name="seed"/> (default settings and structures).</summary>
    /// <param name="seed">The world seed.</param>
    /// <returns>The generator, created on first use.</returns>
    public static ProceduralWorldGenerator ForSeed(ulong seed) => s_bySeed.GetOrAdd(seed, static s => new ProceduralWorldGenerator(s));
}
