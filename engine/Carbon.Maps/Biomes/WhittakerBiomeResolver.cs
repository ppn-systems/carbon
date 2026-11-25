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

        if (elevation < WorldConstants.SeaLevel)
        {
            return BiomeType.ShallowWater;
        }

        // Ocean coastal beach sand in temperate and warm climates (polar ice meets water directly)
        if (elevation < WorldConstants.CoastLevel)
        {
            if (temperature < 0.25f)
            {
                return BiomeType.FrozenTundra;
            }

            return BiomeType.BeachSand;
        }

        // 2. High altitude mountain peaks (elevation > 0.80f)
        if (elevation > 0.80f)
        {
            if (temperature < 0.18f)
            {
                return BiomeType.AlpinePeaks; // Glaciated eternal ice peak
            }

            if (temperature < 0.35f)
            {
                return BiomeType.FrozenTundra; // Alpine snow peak
            }

            if (temperature > 0.75f)
            {
                return BiomeType.VolcanicAshland; // Scorched volcanic peak
            }

            if (temperature > 0.55f && moisture < 0.48f)
            {
                return BiomeType.RedMesa; // Arid canyon plateau
            }

            if (moisture >= 0.70f)
            {
                return BiomeType.BlossomWoods; // Highland fairy blossom valley
            }

            // High altitude temperate mountain: conifer woods or high alpine meadow
            return moisture >= 0.50f ? BiomeType.AncientForest : BiomeType.LushPlains;
        }

        // 3. Sub-zero polar climate
        if (temperature < 0.25f)
        {
            if (elevation >= 0.72f)
            {
                return BiomeType.AlpinePeaks; // Mountain glacier
            }

            return BiomeType.FrozenTundra;
        }

        // 4. Hot arid desert, savanna & red mesa
        if (temperature > 0.70f && moisture < 0.25f)
        {
            return BiomeType.DuneDesert;
        }

        if (temperature >= 0.62f && moisture is >= 0.25f and <= 0.45f)
        {
            return BiomeType.Savanna; // Golden acacia savanna
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

        // 6. Wetlands, blossom woods & swamps
        if (temperature is >= 0.40f and <= 0.65f && moisture is >= 0.70f and < 0.82f)
        {
            return BiomeType.BlossomWoods; // Blooming sakura paradise
        }

        if (temperature is >= 0.40f and <= 0.65f && moisture >= 0.82f)
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
