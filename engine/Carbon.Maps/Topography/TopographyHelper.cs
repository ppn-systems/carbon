// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Maps.Biomes;

namespace Carbon.Maps.Topography;

/// <summary>
/// Domain helper mapping procedural noise elevation and biomes to discrete 2.5D topographical height levels.
/// </summary>
public static class TopographyHelper
{
    /// <summary>
    /// Resolves the discrete height level (0: Lowland, 1: Plateau, 2: Mountain) of a tile.
    /// </summary>
    /// <param name="elevation">Continuous elevation noise value [0..1].</param>
    /// <param name="biome">The resolved biome classification.</param>
    /// <returns>A quantized height level between 0 and 2.</returns>
    public static byte ResolveHeightLevel(float elevation, BiomeType biome)
    {
        if (biome is BiomeType.DeepOcean or BiomeType.ShallowWater)
        {
            return 0;
        }

        if (elevation >= 0.82f)
        {
            return 2;
        }

        if (elevation >= 0.68f)
        {
            return 1;
        }

        return 0;
    }
}
