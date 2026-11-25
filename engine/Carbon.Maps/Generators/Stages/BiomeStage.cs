// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Primitives.Hashing;
using Carbon.Maps.Topography;

namespace Carbon.Maps.Generators.Stages;

/// <summary>Resolves the Whittaker biome, the visual variant and the discrete 2.5D height level.</summary>
public sealed class BiomeStage : ITileStage
{
    private readonly ulong _seed;

    /// <summary>Initializes a new instance of the <see cref="BiomeStage"/> class.</summary>
    /// <param name="seed">World seed.</param>
    public BiomeStage(ulong seed) => _seed = seed;

    /// <inheritdoc/>
    public bool Apply(ref TileSample sample)
    {
        BiomeType biome = WhittakerBiomeResolver.Resolve(sample.Elevation, sample.Temperature, sample.Moisture);
        bool isSolid = BiomeProperties.Get(biome).IsSolid;
        if (!isSolid && biome == BiomeType.VolcanicAshland)
        {
            // Volcanic ashlands are walkable overall, punctuated by impassable obsidian lava fissures.
            isSolid = (SpatialHash.Hash32(_seed, sample.WorldX >> 1, sample.WorldY >> 1, 0x7A7AUL) & 15) == 0;
        }

        byte variant = (byte)(SpatialHash.Hash32(_seed, sample.WorldX, sample.WorldY, 0x0A71UL) & 3);
        byte heightLevel = TopographyHelper.ResolveTerrainHeightLevel(sample.Relief, sample.CoastElevation, biome, isInlandWater: false);

        sample.Tile = new MapTile(biome, sample.Elevation, sample.Temperature, sample.Moisture, isSolid, variant, heightLevel);
        return true;
    }
}
