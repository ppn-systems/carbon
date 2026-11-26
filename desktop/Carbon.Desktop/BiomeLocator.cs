// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;

namespace Carbon.Desktop;

/// <summary>
/// Finds a representative tile of a biome near the spawn, so screenshot captures can frame any biome by name.
/// </summary>
internal static class BiomeLocator
{
    private const int Step = 8;
    private const int MaxRadiusTiles = 6000;
    private const int CoreRadius = 10;

    /// <summary>
    /// Searches outward from <paramref name="originX"/>, <paramref name="originY"/> in square rings for a tile of
    /// <paramref name="biome"/> whose surroundings are the same biome (the interior of a region, not a sliver).
    /// </summary>
    /// <returns><see langword="true"/> when a tile was found within the search radius.</returns>
    public static bool TryFind(ProceduralWorldGenerator generator, BiomeType biome, int originX, int originY, out int tileX, out int tileY)
    {
        for (int r = 0; r <= MaxRadiusTiles; r += Step)
        {
            for (int i = -r; i <= r; i += Step)
            {
                if (IsCore(generator, biome, originX + i, originY - r, out tileX, out tileY) ||
                    IsCore(generator, biome, originX + i, originY + r, out tileX, out tileY) ||
                    IsCore(generator, biome, originX - r, originY + i, out tileX, out tileY) ||
                    IsCore(generator, biome, originX + r, originY + i, out tileX, out tileY))
                {
                    return true;
                }
            }
        }

        tileX = originX;
        tileY = originY;
        return false;
    }

    private static bool IsCore(ProceduralWorldGenerator generator, BiomeType biome, int x, int y, out int tileX, out int tileY)
    {
        tileX = x;
        tileY = y;
        if (generator.SampleTile(x, y).Biome != biome)
        {
            return false;
        }

        // Water biomes are framed at their edge (so the shore and a wading player are in view); land biomes at their core.
        if (biome is BiomeType.ShallowWater)
        {
            return true;
        }

        MapTile n = generator.SampleTile(x, y - CoreRadius);
        MapTile s = generator.SampleTile(x, y + CoreRadius);
        MapTile w = generator.SampleTile(x - CoreRadius, y);
        MapTile e = generator.SampleTile(x + CoreRadius, y);
        return n.Biome == biome && s.Biome == biome && w.Biome == biome && e.Biome == biome;
    }
}
