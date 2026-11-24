// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Maps.Biomes;
using Carbon.Maps.Random;

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

    /// <summary>Relief from which terrain becomes mountain (height level 2).</summary>
    public const float MountainRelief = 0.75f;

    /// <summary>Relief from which terrain becomes plateau (height level 1).</summary>
    public const float PlateauRelief = 0.56f;

    /// <summary>
    /// Resolves the discrete height level from the smooth terrain relief rather than the detailed
    /// elevation, which keeps plateau and mountain outlines broad and free of one-tile specks.
    /// </summary>
    /// <param name="relief">Smooth relief (low-octave elevation plus ridges).</param>
    /// <param name="elevation">Detailed elevation of the tile.</param>
    /// <param name="biome">Resolved biome.</param>
    /// <param name="isInlandWater">Whether the tile is a river or lake, which keeps the level of its terrain.</param>
    /// <returns>0 (lowland), 1 (plateau) or 2 (mountain).</returns>
    public static byte ResolveTerrainHeightLevel(float relief, float elevation, BiomeType biome, bool isInlandWater)
    {
        // Sea, beaches and a coastal buffer stay at sea level so coasts never become sheer sea cliffs.
        if (!isInlandWater && (biome is BiomeType.DeepOcean or BiomeType.ShallowWater or BiomeType.BeachSand || elevation < 0.42f))
        {
            return 0;
        }

        if (relief >= MountainRelief)
        {
            return 2;
        }

        return relief >= PlateauRelief ? (byte)1 : (byte)0;
    }

    /// <summary>
    /// Checks whether a tile coordinate falls on a designated procedural ramp corridor,
    /// used to prevent foliage and boulder obstructions on mountain highway passes.
    /// Spaced organically every ~10-12 tiles along cliff edges.
    /// </summary>
    public static bool IsPotentialRampSlot(int worldTileX, int worldTileY) => IsRampColumn(worldTileX, worldTileY) || IsRampRow(worldTileX, worldTileY);

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
        Func<int, int, bool> isWaterQuery,
        bool isRiverside = false)
    {
        ArgumentNullException.ThrowIfNull(heightQuery);
        ArgumentNullException.ThrowIfNull(isWaterQuery);

        if (currentHeight == 0)
        {
            return RampDirection.None;
        }

        // Paths follow rivers: beside a river every straight cliff tile may carry a ramp, the way trails
        // run up a valley next to a waterfall. Elsewhere ramps appear once per jittered 11-tile block.
        bool rampColumn = isRiverside || IsRampColumn(worldTileX, worldTileY);
        bool rampRow = isRiverside || IsRampRow(worldTileX, worldTileY);

        // 1. South Face Grand Ramp (Incline connects down to South)
        // Must be a straight cliff face: lower south, upper north, flanks solid
        if (rampColumn &&
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
        if (rampColumn &&
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
        if (rampRow &&
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
        if (rampRow &&
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

    // Ramps are 2 tiles wide and appear once per 11-tile block along a cliff. The position inside each
    // block is jittered by a hash of the block, so ramps no longer line up on a visible world grid.
    private const int RampBlock = 11;

    private static bool IsRampColumn(int worldTileX, int worldTileY)
    {
        int block = FloorDiv(worldTileX, RampBlock);
        int offset = 1 + (int)(SpatialHash.Hash32(0x52A3UL, block, FloorDiv(worldTileY, RampBlock * 3), 0x0C01UL) % (RampBlock - 3));
        int slot = worldTileX - (block * RampBlock);
        return slot == offset || slot == offset + 1;
    }

    private static bool IsRampRow(int worldTileX, int worldTileY)
    {
        int block = FloorDiv(worldTileY, RampBlock);
        int offset = 1 + (int)(SpatialHash.Hash32(0x52A3UL, FloorDiv(worldTileX, RampBlock * 3), block, 0x0D02UL) % (RampBlock - 3));
        int slot = worldTileY - (block * RampBlock);
        return slot == offset || slot == offset + 1;
    }

    private static int FloorDiv(int a, int b) => a >= 0 ? a / b : ((a + 1) / b) - 1;
}
