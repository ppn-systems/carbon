// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Abstractions.Configuration;
using Carbon.Maps.Biomes;
using Carbon.Maps.Generators;
using Carbon.Maps.Landmarks;
using Carbon.Maps.Settlements;

namespace Carbon.Application.World;

/// <summary>
/// Resolves the developer-only debug spawn tile of <see cref="WorldDebugOverrides"/> (extracted from
/// <see cref="AuthoritativeWorld.ApplyDebugOverrides"/>): an explicit tile, else the nearest representative tile of a
/// biome, else the nearest point of interest of a type, else the world spawn, plus an offset.
/// </summary>
internal static class DebugSpawnResolver
{
    /// <summary>Search radius in tiles for a debug spawn biome.</summary>
    public const int BiomeSearchRadius = 4000;

    /// <summary>Search radius in sites for a debug spawn point of interest.</summary>
    public const int PoiSearchSites = 12;

    /// <summary>Resolves the spawn tile.</summary>
    /// <param name="generator">World generator.</param>
    /// <param name="worldSpawn">The world's natural spawn tile.</param>
    /// <param name="overrides">The overrides.</param>
    /// <param name="movesSpawn">Whether any override moves the spawn.</param>
    /// <returns>The spawn tile.</returns>
    /// <exception cref="ArgumentException">An unknown biome or point of interest type, or none found in range.</exception>
    public static (int X, int Y) Resolve(ProceduralWorldGenerator generator, (int X, int Y) worldSpawn, WorldDebugOverrides overrides, out bool movesSpawn)
    {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentNullException.ThrowIfNull(overrides);
        (int X, int Y) spawn = worldSpawn;
        if (overrides.SpawnTile is { } tile)
        {
            spawn = tile;
        }
        else if (overrides.SpawnBiome is { } biomeName)
        {
            if (!Enum.TryParse(biomeName, ignoreCase: true, out BiomeType biome) || !Enum.IsDefined(biome))
            {
                throw new ArgumentException($"Unknown biome '{biomeName}'.", nameof(overrides));
            }

            if (!BiomeLocator.TryFind(generator, biome, spawn.X, spawn.Y, BiomeSearchRadius, out int bx, out int by))
            {
                throw new ArgumentException($"No {biome} tile within {BiomeSearchRadius} tiles of spawn.", nameof(overrides));
            }

            spawn = (bx, by);
        }
        else if (overrides.SpawnPoi is { } poiName)
        {
            if (!Enum.TryParse(poiName, ignoreCase: true, out PointOfInterestType poiType) || !Enum.IsDefined(poiType) ||
                poiType == PointOfInterestType.None)
            {
                throw new ArgumentException($"Unknown point of interest type '{poiName}'.", nameof(overrides));
            }

            spawn = FindNearestPoi(generator, poiType, spawn)
                ?? throw new ArgumentException($"No {poiType} within {PoiSearchSites} sites of spawn.", nameof(overrides));
        }

        movesSpawn = overrides.SpawnTile.HasValue || overrides.SpawnBiome is not null || overrides.SpawnPoi is not null ||
            overrides.SpawnOffset != default;
        return (spawn.X + overrides.SpawnOffset.X, spawn.Y + overrides.SpawnOffset.Y);
    }

    // Nearest point of interest of a type by site rings around the spawn: in front of a village's market stall, else
    // three tiles south of its centre.
    private static (int X, int Y)? FindNearestPoi(ProceduralWorldGenerator generator, PointOfInterestType type, (int X, int Y) from)
    {
        int sx0 = (int)MathF.Floor(from.X / (float)PointOfInterestResolver.SiteSize);
        int sy0 = (int)MathF.Floor(from.Y / (float)PointOfInterestResolver.SiteSize);
        for (int ring = 0; ring <= PoiSearchSites; ring++)
        {
            for (int dy = -ring; dy <= ring; dy++)
            {
                for (int dx = -ring; dx <= ring; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring)
                    {
                        continue;
                    }

                    PointOfInterest poi = generator.Landmarks.GetSite(sx0 + dx, sy0 + dy);
                    if (poi.Type != type)
                    {
                        continue;
                    }

                    // Villages: in front of the market stall, so the square, its buildings and the merchant are in view.
                    foreach (VillagePlot plot in generator.Landmarks.GetLayout(sx0 + dx, sy0 + dy).Plots)
                    {
                        if (plot.Role == VillageBuildingRole.Stall)
                        {
                            (int fx, int fy) = VillageBlueprint.FrontTile(VillageBuildingCatalog.Default, plot);
                            return (fx, fy + 1);
                        }
                    }

                    return (poi.TileX, poi.TileY + 3);
                }
            }
        }

        return null;
    }
}
