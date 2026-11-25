// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Maps.Biomes;

namespace Carbon.Maps.Topography;

/// <summary>
/// Domain helper mapping procedural noise elevation and biomes to discrete 2.5D topographical height levels
/// and procedural mountain passes / ramp inclines.
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
        if (biome is BiomeType.DeepOcean or BiomeType.ShallowWater or BiomeType.BeachSand)
        {
            return 0;
        }

        if (biome is BiomeType.AlpinePeaks || elevation >= 0.70f)
        {
            return 2;
        }

        if (elevation >= 0.54f)
        {
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// Checks whether a tile coordinate falls on a designated procedural ramp corridor,
    /// used to prevent foliage and boulder obstructions on mountain highway passes.
    /// Spaced organically every ~10-12 tiles along cliff edges.
    /// </summary>
    public static bool IsPotentialRampSlot(int worldTileX, int worldTileY)
    {
        int slotX = ((worldTileX % 11) + 11) % 11;
        int slotY = ((worldTileY % 11) + 11) % 11;
        return slotX is 5 or 6 || slotY is 5 or 6;
    }

    /// <summary>
    /// Evaluates whether a cliff tile naturally forms a walkable mountain pass / ramp incline
    /// connecting two topographical height levels in the procedural infinite world.
    /// Spaced regularly along straight cliff walls; strictly excludes corners to preserve natural geology.
    /// </summary>
    public static RampDirection ResolveProceduralRamp(
        int worldTileX,
        int worldTileY,
        byte currentHeight,
        Func<int, int, byte> heightQuery,
        Func<int, int, bool> isWaterQuery)
    {
        ArgumentNullException.ThrowIfNull(heightQuery);
        ArgumentNullException.ThrowIfNull(isWaterQuery);

        if (currentHeight == 0)
        {
            return RampDirection.None;
        }

        int slotX = ((worldTileX % 11) + 11) % 11;
        int slotY = ((worldTileY % 11) + 11) % 11;

        // 1. South Face Grand Ramp (Incline connects down to South)
        // Must be a straight cliff face: lower south, upper north, flanks solid
        if (slotX is 5 or 6 &&
            heightQuery(worldTileX, worldTileY + 1) == currentHeight - 1 &&
            heightQuery(worldTileX, worldTileY - 1) >= currentHeight &&
            heightQuery(worldTileX - 1, worldTileY) >= currentHeight &&
            heightQuery(worldTileX + 1, worldTileY) >= currentHeight &&
            !isWaterQuery(worldTileX, worldTileY + 1) &&
            !isWaterQuery(worldTileX, worldTileY))
        {
            return RampDirection.South;
        }

        // 2. North Face Grand Ramp (Incline connects down to North)
        if (slotX is 5 or 6 &&
            heightQuery(worldTileX, worldTileY - 1) == currentHeight - 1 &&
            heightQuery(worldTileX, worldTileY + 1) >= currentHeight &&
            heightQuery(worldTileX - 1, worldTileY) >= currentHeight &&
            heightQuery(worldTileX + 1, worldTileY) >= currentHeight &&
            !isWaterQuery(worldTileX, worldTileY - 1) &&
            !isWaterQuery(worldTileX, worldTileY))
        {
            return RampDirection.North;
        }

        // 3. East Face Grand Ramp (Incline connects down to East)
        if (slotY is 5 or 6 &&
            heightQuery(worldTileX + 1, worldTileY) == currentHeight - 1 &&
            heightQuery(worldTileX - 1, worldTileY) >= currentHeight &&
            heightQuery(worldTileX, worldTileY - 1) >= currentHeight &&
            heightQuery(worldTileX, worldTileY + 1) >= currentHeight &&
            !isWaterQuery(worldTileX + 1, worldTileY) &&
            !isWaterQuery(worldTileX, worldTileY))
        {
            return RampDirection.East;
        }

        // 4. West Face Grand Ramp (Incline connects down to West)
        if (slotY is 5 or 6 &&
            heightQuery(worldTileX - 1, worldTileY) == currentHeight - 1 &&
            heightQuery(worldTileX + 1, worldTileY) >= currentHeight &&
            heightQuery(worldTileX, worldTileY - 1) >= currentHeight &&
            heightQuery(worldTileX, worldTileY + 1) >= currentHeight &&
            !isWaterQuery(worldTileX - 1, worldTileY) &&
            !isWaterQuery(worldTileX, worldTileY))
        {
            return RampDirection.West;
        }

        return RampDirection.None;
    }
}
