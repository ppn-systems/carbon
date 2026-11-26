// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Maps.Biomes;

namespace Carbon.Creatures.Spawning;

/// <summary>
/// Resolves the biome at a world position for biome-aware spawning. Implementations must be deterministic.
/// </summary>
public interface IBiomeSampler
{
    /// <summary>
    /// Gets the biome at <paramref name="worldPosition"/>.
    /// </summary>
    /// <param name="worldPosition">World position.</param>
    /// <returns>Biome classification.</returns>
    BiomeType SampleBiome(Vector2 worldPosition);
}
