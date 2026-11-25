// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;

namespace Carbon.Maps.Streaming;

/// <summary>Finds a safe natural spawn tile: the nearest flat, unobstructed LushPlains tile spiralling out from the origin.</summary>
internal static class SpawnLocator
{
    /// <summary>Returns the spawn tile for a generator.</summary>
    /// <param name="generator">The world generator.</param>
    /// <param name="frequency">Macro noise frequency.</param>
    /// <returns>The spawn tile coordinate.</returns>
    public static (int X, int Y) Locate(ProceduralWorldGenerator generator, float frequency)
    {
        // Primary search: dry, flat, unobstructed LushPlains within r < 600.
        if (TryFindPerimeterMatch(generator, frequency, maxRadius: 600, requireLushPlains: true, out (int X, int Y) spawn))
        {
            return spawn;
        }

        // Fallback for extreme ocean/frozen seeds: any dry, flat, unobstructed land tile within r < 2400.
        if (TryFindPerimeterMatch(generator, frequency, maxRadius: 2400, requireLushPlains: false, out spawn))
        {
            return spawn;
        }

        return (0, 0);
    }

    private static bool TryFindPerimeterMatch(
        ProceduralWorldGenerator generator,
        float frequency,
        int maxRadius,
        bool requireLushPlains,
        out (int X, int Y) result)
    {
        if (IsSafeSpawn(generator, frequency, 0, 0, requireLushPlains))
        {
            result = (0, 0);
            return true;
        }

        for (int r = 4; r < maxRadius; r += 4)
        {
            for (int dx = -r; dx <= r; dx += 4)
            {
                if (IsSafeSpawn(generator, frequency, dx, -r, requireLushPlains))
                {
                    result = (dx, -r);
                    return true;
                }

                if (IsSafeSpawn(generator, frequency, dx, r, requireLushPlains))
                {
                    result = (dx, r);
                    return true;
                }
            }

            for (int dy = -r + 4; dy <= r - 4; dy += 4)
            {
                if (IsSafeSpawn(generator, frequency, -r, dy, requireLushPlains))
                {
                    result = (-r, dy);
                    return true;
                }

                if (IsSafeSpawn(generator, frequency, r, dy, requireLushPlains))
                {
                    result = (r, dy);
                    return true;
                }
            }
        }

        result = default;
        return false;
    }

    private static bool IsSafeSpawn(
        ProceduralWorldGenerator generator,
        float frequency,
        int x,
        int y,
        bool requireLushPlains)
    {
        MapTile t = generator.SampleTile(x, y, frequency);
        if (t.IsSolid || t.HeightLevel != 0 || t.Biome is BiomeType.DeepOcean or BiomeType.ShallowWater)
        {
            return false;
        }

        if (requireLushPlains && t.Biome != BiomeType.LushPlains)
        {
            return false;
        }

        // Verify 4 cardinal neighbors at ±2 tiles are dry and level 0 so spawn is never on a 1-tile island or cliff lip.
        if (!IsClearNeighbor(generator, frequency, x + 2, y) ||
            !IsClearNeighbor(generator, frequency, x - 2, y) ||
            !IsClearNeighbor(generator, frequency, x, y + 2) ||
            !IsClearNeighbor(generator, frequency, x, y - 2))
        {
            return false;
        }

        return !generator.Landmarks.IsTileBlocked(x, y);
    }

    private static bool IsClearNeighbor(ProceduralWorldGenerator generator, float frequency, int x, int y)
    {
        MapTile n = generator.SampleTile(x, y, frequency);
        return !n.IsSolid && n.HeightLevel == 0 && n.Biome is not (BiomeType.DeepOcean or BiomeType.ShallowWater);
    }
}
