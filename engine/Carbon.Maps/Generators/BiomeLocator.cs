// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;

namespace Carbon.Maps.Generators;

/// <summary>
/// Finds a representative tile of a biome near a starting point (screenshot review, debug spawn overrides).
/// The search walks square rings outwards from the origin on a coarse lattice and returns the first tile
/// that sits well inside the biome. Pure and deterministic: the same seed always yields the same tile.
/// </summary>
public static class BiomeLocator
{
    /// <summary>Lattice step of the outward search, in tiles.</summary>
    public const int SearchStep = 6;

    /// <summary>Distance, in tiles, at which the neighbourhood must still be the same biome.</summary>
    public const int InteriorReach = 10;

    private const int InteriorScoreMax = 12;

    /// <summary>
    /// Searches outwards from (<paramref name="originX"/>, <paramref name="originY"/>) for a tile of
    /// <paramref name="biome"/> whose neighbours ±<see cref="InteriorReach"/> tiles away share the biome.
    /// <see cref="BiomeType.ShallowWater"/> is instead framed at a shore: a shallow tile with dry land two to
    /// three tiles away (wading depth) and none adjacent. Biomes too small for a full interior return the
    /// tile with the most same-biome neighbours (at least half of them) instead.
    /// </summary>
    /// <param name="generator">The world generator.</param>
    /// <param name="biome">The biome to find.</param>
    /// <param name="originX">Search origin X (world tiles).</param>
    /// <param name="originY">Search origin Y (world tiles).</param>
    /// <param name="maxRadius">Maximum search radius in tiles.</param>
    /// <param name="tileX">The tile found.</param>
    /// <param name="tileY">The tile found.</param>
    /// <returns><c>true</c> when a tile was found within <paramref name="maxRadius"/>.</returns>
    public static bool TryFind(
        ProceduralWorldGenerator generator,
        BiomeType biome,
        int originX,
        int originY,
        int maxRadius,
        out int tileX,
        out int tileY)
    {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRadius);

        // Biomes too small to hold a full interior (volcanic peaks) fall back to the best-surrounded tile.
        int bestScore = (InteriorScoreMax / 2) - 1;
        tileX = originX;
        tileY = originY;
        for (int ring = 0; ring * SearchStep <= maxRadius; ring++)
        {
            int r = ring * SearchStep;
            int side = Math.Max(1, ring * 2);
            for (int i = 0; i < side * 4; i++)
            {
                // Walk the ring's perimeter: top edge, right edge, bottom edge, left edge.
                int edge = ring == 0 ? 0 : i / side;
                int k = ring == 0 ? 0 : i % side * SearchStep;
                (int x, int y) = edge switch
                {
                    0 => (originX - r + k, originY - r),
                    1 => (originX + r, originY - r + k),
                    2 => (originX + r - k, originY + r),
                    _ => (originX - r, originY + r - k),
                };

                int score = Score(generator, biome, x, y);
                if (score == InteriorScoreMax)
                {
                    tileX = x;
                    tileY = y;
                    return true;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    tileX = x;
                    tileY = y;
                }

                if (ring == 0)
                {
                    break;
                }
            }
        }

        return bestScore >= InteriorScoreMax / 2;
    }

    /// <summary>
    /// Gets whether (<paramref name="x"/>, <paramref name="y"/>) is a representative tile of <paramref name="biome"/>
    /// (see <see cref="TryFind"/>).
    /// </summary>
    /// <param name="generator">The world generator.</param>
    /// <param name="biome">The biome.</param>
    /// <param name="x">World tile X.</param>
    /// <param name="y">World tile Y.</param>
    /// <returns><c>true</c> when the tile qualifies.</returns>
    public static bool IsRepresentative(ProceduralWorldGenerator generator, BiomeType biome, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(generator);
        return Score(generator, biome, x, y) == InteriorScoreMax;
    }

    // Number of interior probes that share the biome (InteriorScoreMax = fully inside), or -1 when the tile
    // itself does not qualify.
    private static int Score(ProceduralWorldGenerator generator, BiomeType biome, int x, int y)
    {
        MapTile centre = generator.SampleTile(x, y);
        if (centre.Biome != biome)
        {
            return -1;
        }

        if (biome == BiomeType.ShallowWater)
        {
            return IsWadingShore(generator, x, y) ? InteriorScoreMax : -1;
        }

        // Deep water is solid by nature; anywhere else the player must be able to stand on the tile.
        if (centre.IsSolid && biome != BiomeType.DeepOcean)
        {
            return -1;
        }

        // Eight compass points at full reach plus four at half reach.
        ReadOnlySpan<int> dx = [1, 1, 0, -1, -1, -1, 0, 1, 1, 0, -1, 0];
        ReadOnlySpan<int> dy = [0, 1, 1, 1, 0, -1, -1, -1, 0, 1, 0, -1];
        int score = 0;
        for (int i = 0; i < dx.Length; i++)
        {
            int reach = i < 8 ? InteriorReach : InteriorReach / 2;
            if (generator.SampleTile(x + (dx[i] * reach), y + (dy[i] * reach)).Biome == biome)
            {
                score++;
            }
        }

        return score;
    }

    // Shallow water with no dry tile adjacent but dry land two or three tiles away, and more shallow water
    // around it: the player stands wading near a visible shoreline.
    private static bool IsWadingShore(ProceduralWorldGenerator generator, int x, int y)
    {
        bool landNear = false;
        int shallow = 0;
        for (int dy = -3; dy <= 3; dy++)
        {
            for (int dx = -3; dx <= 3; dx++)
            {
                if (dx == 0 && dy == 0)
                {
                    continue;
                }

                BiomeType b = generator.SampleTile(x + dx, y + dy).Biome;
                bool water = b is BiomeType.ShallowWater or BiomeType.DeepOcean;
                int d = Math.Max(Math.Abs(dx), Math.Abs(dy));
                if (!water && d <= 1)
                {
                    return false;
                }

                landNear |= !water;
                shallow += b == BiomeType.ShallowWater ? 1 : 0;
            }
        }

        return landNear && shallow >= 16;
    }
}
