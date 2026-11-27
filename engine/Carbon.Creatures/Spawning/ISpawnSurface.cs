// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Maps.Biomes;
using Carbon.Maps.Hydrology;

namespace Carbon.Creatures.Spawning;

/// <summary>
/// Answers whether a monster may stand at a world position and which biome it is in. Implementations must be
/// deterministic for a given world state.
/// </summary>
public interface ISpawnSurface
{
    /// <summary>
    /// Samples <paramref name="worldPosition"/> for spawning.
    /// </summary>
    /// <param name="worldPosition">Candidate world position.</param>
    /// <param name="biome">Biome at the position.</param>
    /// <returns><see langword="false"/> if the position is blocked (solid tile, water, solid prop).</returns>
    bool TrySampleSpawnBiome(Vector2 worldPosition, out BiomeType biome);

    /// <summary>
    /// Samples <paramref name="worldPosition"/> for a water creature (ADR-032 D14): open water free of props and decks.
    /// Surfaces without water have none.
    /// </summary>
    /// <param name="worldPosition">Candidate world position.</param>
    /// <param name="tier">Depth tier of the water at the position.</param>
    /// <returns><see langword="true"/> in open water.</returns>
    bool TrySampleSpawnWater(Vector2 worldPosition, out WaterDepthTier tier)
    {
        tier = WaterDepthTier.Dry;
        return false;
    }
}
