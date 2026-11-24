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
        // DeepOcean: Impassable deep navy blue (0x0f172aFF)
        new(BiomeType.DeepOcean, IsSolid: true, MoveSpeedMultiplier: 0.0f, ColorRgba: 0x0F172AFF),

        // ShallowWater: Walkable light cyan (0x38bdf8FF)
        new(BiomeType.ShallowWater, IsSolid: false, MoveSpeedMultiplier: 0.7f, ColorRgba: 0x38BDF8FF),

        // BeachSand: Golden sand (0xfef08aFF)
        new(BiomeType.BeachSand, IsSolid: false, MoveSpeedMultiplier: 0.9f, ColorRgba: 0xFEF08AFF),

        // LushPlains: Vibrant green (0x22c55eFF)
        new(BiomeType.LushPlains, IsSolid: false, MoveSpeedMultiplier: 1.0f, ColorRgba: 0x22C55EFF),

        // AncientForest: Deep moss green (0x15803dFF)
        new(BiomeType.AncientForest, IsSolid: false, MoveSpeedMultiplier: 0.95f, ColorRgba: 0x15803DFF),

        // MistySwamp: Murky slate olive (0x334155FF)
        new(BiomeType.MistySwamp, IsSolid: false, MoveSpeedMultiplier: 0.6f, ColorRgba: 0x334155FF),

        // DuneDesert: Warm orange sand (0xea580cFF)
        new(BiomeType.DuneDesert, IsSolid: false, MoveSpeedMultiplier: 0.85f, ColorRgba: 0xEA580CFF),

        // RedMesa: Deep crimson canyon (0x991b1bFF)
        new(BiomeType.RedMesa, IsSolid: false, MoveSpeedMultiplier: 0.9f, ColorRgba: 0x991B1BFF),

        // TropicalJungle: Rich emerald (0x047857FF)
        new(BiomeType.TropicalJungle, IsSolid: false, MoveSpeedMultiplier: 0.8f, ColorRgba: 0x047857FF),

        // FrozenTundra: Glacial white-blue (0xe0f2feFF)
        new(BiomeType.FrozenTundra, IsSolid: false, MoveSpeedMultiplier: 1.15f, ColorRgba: 0xE0F2FEFF),

        // VolcanicAshland: Impassable dark obsidian basalt (0x18181bFF)
        new(BiomeType.VolcanicAshland, IsSolid: true, MoveSpeedMultiplier: 0.0f, ColorRgba: 0x18181BFF)
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
