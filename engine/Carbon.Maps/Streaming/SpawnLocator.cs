// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;

namespace Carbon.Maps.Streaming;

/// <summary>Finds a safe natural spawn tile: the nearest dry LushPlains tile spiralling out from the origin.</summary>
internal static class SpawnLocator
{
    /// <summary>Returns the spawn tile for a generator.</summary>
    /// <param name="generator">The world generator.</param>
    /// <param name="frequency">Macro noise frequency.</param>
    /// <returns>The spawn tile coordinate.</returns>
    public static (int X, int Y) Locate(ProceduralWorldGenerator generator, float frequency)
    {
        // Spiral outward from (0, 0) to find pristine natural land in LushPlains
        for (int r = 0; r < 500; r += 4)
        {
            for (int dy = -r; dy <= r; dy += 4)
            {
                for (int dx = -r; dx <= r; dx += 4)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r)
                    {
                        continue;
                    }

                    MapTile t = generator.SampleTile(dx, dy, frequency);
                    if (t.Biome == BiomeType.LushPlains && !t.IsSolid)
                    {
                        return (dx, dy);
                    }
                }
            }
        }

        return (0, 0);
    }
}
