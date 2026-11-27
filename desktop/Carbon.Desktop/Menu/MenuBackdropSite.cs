// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Maps.Biomes;

namespace Carbon.Desktop.Menu;

/// <summary>
/// Picks where the menu backdrop looks: the most scenic spot near the spawn. A spot scores for every distinct biome its
/// view shows and for showing some water (a shore or river, not open sea). Ties keep the spot nearest the spawn, so a
/// seed always gives the same backdrop. Runs once on the worker thread that generates the backdrop world.
/// </summary>
internal static class MenuBackdropSite
{
    /// <summary>How far from the spawn candidates go, in tiles (8 chunks).</summary>
    public const int SearchRadiusTiles = 256;

    /// <summary>Distance between candidates, in tiles (one chunk).</summary>
    public const int StepTiles = 32;

    private const int SamplesX = 9;
    private const int SamplesY = 6;
    private const int SpanXTiles = 88;
    private const int SpanYTiles = 50;

    /// <summary>Returns the chosen centre tile; <paramref name="biomeAt"/> gives the biome of a world tile.</summary>
    public static (int X, int Y) Choose(Func<int, int, BiomeType> biomeAt, (int X, int Y) spawn)
    {
        ArgumentNullException.ThrowIfNull(biomeAt);
        (int X, int Y) best = spawn;
        int bestScore = Score(biomeAt, spawn.X, spawn.Y);
        long bestDistance = 0;
        for (int dy = -SearchRadiusTiles; dy <= SearchRadiusTiles; dy += StepTiles)
        {
            for (int dx = -SearchRadiusTiles; dx <= SearchRadiusTiles; dx += StepTiles)
            {
                int score = Score(biomeAt, spawn.X + dx, spawn.Y + dy);
                long distance = ((long)dx * dx) + ((long)dy * dy);
                if (score > bestScore || (score == bestScore && distance < bestDistance))
                {
                    best = (spawn.X + dx, spawn.Y + dy);
                    bestScore = score;
                    bestDistance = distance;
                }
            }
        }

        return best;
    }

    /// <summary>Scores the view centred on a tile: distinct biomes, plus 3 when water covers 8 %–45 % of it.</summary>
    public static int Score(Func<int, int, BiomeType> biomeAt, int centreX, int centreY)
    {
        ArgumentNullException.ThrowIfNull(biomeAt);
        Span<bool> seen = stackalloc bool[256];
        int distinct = 0;
        int water = 0;
        for (int sy = 0; sy < SamplesY; sy++)
        {
            for (int sx = 0; sx < SamplesX; sx++)
            {
                int x = centreX - (SpanXTiles / 2) + (sx * SpanXTiles / (SamplesX - 1));
                int y = centreY - (SpanYTiles / 2) + (sy * SpanYTiles / (SamplesY - 1));
                BiomeType biome = biomeAt(x, y);
                if (!seen[(byte)biome])
                {
                    seen[(byte)biome] = true;
                    distinct++;
                }

                if (biome is BiomeType.DeepOcean or BiomeType.ShallowWater)
                {
                    water++;
                }
            }
        }

        float share = water / (float)(SamplesX * SamplesY);
        return distinct + (share is >= 0.08f and <= 0.45f ? 3 : 0);
    }
}
