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
        // DeepOcean: Deep sapphire navy (0x0D3B66FF)
        new(BiomeType.DeepOcean, IsSolid: true, MoveSpeedMultiplier: 0.0f, ColorRgba: 0x0D3B66FF),

        // ShallowWater: Vibrant turquoise coastal / river water (0x2BA8D6FF)
        new(BiomeType.ShallowWater, IsSolid: false, MoveSpeedMultiplier: 0.7f, ColorRgba: 0x2BA8D6FF),

        // BeachSand: Soft natural warm beach sand (0xE4CE9FFF)
        new(BiomeType.BeachSand, IsSolid: false, MoveSpeedMultiplier: 0.9f, ColorRgba: 0xE4CE9FFF),

        // LushPlains: Rich natural meadow green (0x3D7A46FF)
        new(BiomeType.LushPlains, IsSolid: false, MoveSpeedMultiplier: 1.0f, ColorRgba: 0x3D7A46FF),

        // AncientForest: Deep conifer moss green (0x244B30FF)
        new(BiomeType.AncientForest, IsSolid: false, MoveSpeedMultiplier: 0.95f, ColorRgba: 0x244B30FF),

        // MistySwamp: Murky slate olive (0x3E4A3EFF)
        new(BiomeType.MistySwamp, IsSolid: false, MoveSpeedMultiplier: 0.6f, ColorRgba: 0x3E4A3EFF),

        // DuneDesert: Warm golden desert sandstone (0xD49B6AFF)
        new(BiomeType.DuneDesert, IsSolid: false, MoveSpeedMultiplier: 0.85f, ColorRgba: 0xD49B6AFF),

        // RedMesa: Warm terracotta canyon clay (0xA4503EFF)
        new(BiomeType.RedMesa, IsSolid: false, MoveSpeedMultiplier: 0.9f, ColorRgba: 0xA4503EFF),

        // TropicalJungle: Rich dense rainforest emerald (0x1E5936FF)
        new(BiomeType.TropicalJungle, IsSolid: false, MoveSpeedMultiplier: 0.8f, ColorRgba: 0x1E5936FF),

        // FrozenTundra: Glacial frost pale cyan-white (0xCFE4F0FF)
        new(BiomeType.FrozenTundra, IsSolid: false, MoveSpeedMultiplier: 1.15f, ColorRgba: 0xCFE4F0FF),

        // VolcanicAshland: Dark obsidian charcoal basalt (0x2B292EFF)
        new(BiomeType.VolcanicAshland, IsSolid: true, MoveSpeedMultiplier: 0.0f, ColorRgba: 0x2B292EFF)
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
