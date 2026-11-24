// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Maps.Biomes;

/// <summary>
/// Resolves continuous climate parameters (Elevation, Temperature, Moisture) into discrete Whittaker biomes.
/// Hot path zero allocation.
/// </summary>
public static class WhittakerBiomeResolver
{
    /// <summary>
    /// Resolves biome type from normalized climate inputs in the range [0.0, 1.0].
    /// </summary>
    /// <param name="elevation">Normalized elevation height [0.0, 1.0].</param>
    /// <param name="temperature">Normalized temperature [0.0, 1.0] (0 = sub-zero, 1 = scorching).</param>
    /// <param name="moisture">Normalized moisture rainfall [0.0, 1.0] (0 = arid, 1 = deluge).</param>
    /// <returns>Resolved <see cref="BiomeType"/>.</returns>
    public static BiomeType Resolve(float elevation, float temperature, float moisture)
    {
        // 1. Water boundaries
        if (elevation < 0.22f)
        {
            return BiomeType.DeepOcean;
        }

        if (elevation < 0.32f)
        {
            return BiomeType.ShallowWater;
        }

        if (elevation < 0.38f)
        {
            return BiomeType.BeachSand;
        }

        // 2. High altitude mountain peaks
        if (elevation > 0.80f)
        {
            if (temperature < 0.35f)
            {
                return BiomeType.FrozenTundra;
            }

            if (temperature > 0.75f)
            {
                return BiomeType.VolcanicAshland;
            }

            return BiomeType.RedMesa;
        }

        // 3. Sub-zero polar climate
        if (temperature < 0.25f)
        {
            return BiomeType.FrozenTundra;
        }

        // 4. Hot arid desert & mesa
        if (temperature > 0.70f && moisture < 0.30f)
        {
            return BiomeType.DuneDesert;
        }

        if (temperature > 0.60f && moisture < 0.48f)
        {
            return BiomeType.RedMesa;
        }

        // 5. Tropical jungle
        if (temperature > 0.65f && moisture >= 0.70f)
        {
            return BiomeType.TropicalJungle;
        }

        // 6. Wetlands & swamps
        if (temperature is >= 0.40f and <= 0.65f && moisture >= 0.75f)
        {
            return BiomeType.MistySwamp;
        }

        // 7. Woodlands & forests
        if (moisture >= 0.50f)
        {
            return BiomeType.AncientForest;
        }

        // 8. Default starter plains
        return BiomeType.LushPlains;
    }
}
