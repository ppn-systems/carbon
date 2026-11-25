// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Maps.Biomes;

/// <summary>
/// Physical simulation and procedural visual metadata for a biome classification.
/// Zero heap allocation unmanaged record struct.
/// </summary>
public readonly record struct BiomeProperties(
    BiomeType Type,
    bool IsSolid,
    float MoveSpeedMultiplier,
    uint ColorRgba)
{
    private static readonly BiomeProperties[] s_table =
    [
        // DeepOcean: Oceanic indigo-navy (0x1B3854FF)
        new(BiomeType.DeepOcean, IsSolid: true, MoveSpeedMultiplier: 0.0f, ColorRgba: 0x1B3854FF),

        // ShallowWater: Painterly clear turquoise water (0x389BB8FF)
        new(BiomeType.ShallowWater, IsSolid: false, MoveSpeedMultiplier: 0.7f, ColorRgba: 0x389BB8FF),

        // BeachSand: Soft warm natural beach sand (0xDEC89AFF)
        new(BiomeType.BeachSand, IsSolid: false, MoveSpeedMultiplier: 0.9f, ColorRgba: 0xDEC89AFF),

        // LushPlains: Rich natural meadow green (0x4A824BFF)
        new(BiomeType.LushPlains, IsSolid: false, MoveSpeedMultiplier: 1.0f, ColorRgba: 0x4A824BFF),

        // AncientForest: Deep conifer moss green (0x2C5E3BFF)
        new(BiomeType.AncientForest, IsSolid: false, MoveSpeedMultiplier: 0.95f, ColorRgba: 0x2C5E3BFF),

        // MistySwamp: Atmospheric mossy peat olive (0x445842FF)
        new(BiomeType.MistySwamp, IsSolid: false, MoveSpeedMultiplier: 0.6f, ColorRgba: 0x445842FF),

        // DuneDesert: Warm golden desert sandstone (0xD6A56EFF)
        new(BiomeType.DuneDesert, IsSolid: false, MoveSpeedMultiplier: 0.85f, ColorRgba: 0xD6A56EFF),

        // RedMesa: Warm layered terracotta canyon clay (0xB05C46FF)
        new(BiomeType.RedMesa, IsSolid: false, MoveSpeedMultiplier: 0.9f, ColorRgba: 0xB05C46FF),

        // TropicalJungle: Rich dense rainforest jade (0x23683DFF)
        new(BiomeType.TropicalJungle, IsSolid: false, MoveSpeedMultiplier: 0.8f, ColorRgba: 0x23683DFF),

        // FrozenTundra: Glacial frost pale snow (0xDFECF4FF)
        new(BiomeType.FrozenTundra, IsSolid: false, MoveSpeedMultiplier: 0.90f, ColorRgba: 0xDFECF4FF),

        // VolcanicAshland: Walkable dark obsidian charcoal ashlands (lava fissures are solid per tile)
        new(BiomeType.VolcanicAshland, IsSolid: false, MoveSpeedMultiplier: 0.80f, ColorRgba: 0x353338FF),

        // AlpinePeaks: Glaciated icy blue-white mountain peaks (0xD8E8F5FF)
        new(BiomeType.AlpinePeaks, IsSolid: false, MoveSpeedMultiplier: 0.85f, ColorRgba: 0xD8E8F5FF),

        // Savanna: Golden amber savanna prairie (0xC4A35AFF)
        new(BiomeType.Savanna, IsSolid: false, MoveSpeedMultiplier: 1.05f, ColorRgba: 0xC4A35AFF),

        // BlossomWoods: Vibrant emerald jade with blossom radiance (0x488C64FF)
        new(BiomeType.BlossomWoods, IsSolid: false, MoveSpeedMultiplier: 1.0f, ColorRgba: 0x488C64FF)
    ];

    /// <summary>
    /// Retrieves physical and visual properties for a given biome.
    /// </summary>
    /// <param name="biome">Biome identifier.</param>
    /// <returns>Biome property descriptor.</returns>
    public static ref readonly BiomeProperties Get(BiomeType biome)
    {
        int index = (int)biome;
        if (index >= 0 && index < s_table.Length)
        {
            return ref s_table[index];
        }

        return ref s_table[0];
    }
}
