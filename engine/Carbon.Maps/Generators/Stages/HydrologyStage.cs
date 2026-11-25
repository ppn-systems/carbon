// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Hydrology;
using Carbon.Maps.Noise;
using Carbon.Maps.Random;
using Carbon.Maps.Topography;

namespace Carbon.Maps.Generators.Stages;

/// <summary>
/// Rivers, lakes and oases. Water tiles are finalised here; land near water is lowered and moistened
/// with a smooth, width-scaled falloff so banks blend instead of forming trenches.
/// </summary>
public sealed class HydrologyStage : ITileStage
{
    private readonly ulong _seed;
    private readonly IRiverDistanceSource _rivers;
    private readonly FractalNoise2D _lakes;

    /// <summary>Initializes a new instance of the <see cref="HydrologyStage"/> class.</summary>
    /// <param name="seed">World seed.</param>
    /// <param name="rivers">The river network.</param>
    public HydrologyStage(ulong seed, IRiverDistanceSource rivers)
    {
        _seed = seed;
        _rivers = rivers ?? throw new ArgumentNullException(nameof(rivers));
        _lakes = new FractalNoise2D(seed ^ 0x9876543210ABCDEFUL);
    }

    /// <inheritdoc/>
    public bool Apply(ref TileSample sample)
    {
        sample.RiverDistance = float.NaN;

        // Rivers discharge into the sea; open ocean is never altered.
        if (sample.Elevation < WorldConstants.SeaLevel)
        {
            return true;
        }

        float riverDist = _rivers.GetDistance(sample.WorldX, sample.WorldY);
        sample.RiverDistance = riverDist;

        // Desert oasis springs in arid dunes
        float lakeNoise = _lakes.Evaluate((sample.Fx * 3.6f) + 120f, (sample.Fy * 3.6f) + 120f, octaves: 2);
        bool isOasis = sample.Temperature > 0.68f && sample.RawMoisture < 0.28f &&
            sample.Elevation >= 0.42f && sample.Elevation <= 0.62f && lakeNoise > 0.86f;

        if (riverDist <= WorldConstants.RiverChannel || isOasis)
        {
            // River channel or lake: a continuous water surface sitting on the terrain it crosses
            // (the renderer draws waterfalls where it leaves a plateau).
            byte variant = (byte)(SpatialHash.Hash32(_seed, sample.WorldX, sample.WorldY, 0x0A71UL) & 3);
            sample.Tile = new MapTile(
                BiomeType.ShallowWater,
                Elevation: 0.28f,
                sample.Temperature,
                Moisture: 0.85f,
                IsSolid: false,
                variant,
                HeightLevel: TopographyHelper.ResolveTerrainHeightLevel(sample.Relief, sample.CoastElevation, BiomeType.ShallowWater, isInlandWater: true));
            return false;
        }

        if (riverDist < WorldConstants.RiparianReach)
        {
            // Riparian zone: ease the ground down towards the water and wet it; big rivers get wider valleys.
            float k = 1f - Math.Clamp((riverDist - WorldConstants.RiverChannel) / (WorldConstants.RiparianReach - WorldConstants.RiverChannel), 0f, 1f);
            k = k * k * (3f - (2f * k));
            float elevation = MathF.Max(sample.Elevation - (0.08f * k), MathF.Min(sample.Elevation, 0.36f));
            if (riverDist < 3.0f)
            {
                elevation = MathF.Min(elevation, 0.37f); // narrow sand or gravel bank
            }

            sample.Elevation = elevation;
            sample.Moisture = Math.Clamp(sample.Moisture + (0.30f * k), 0f, 1f);
        }

        return true;
    }
}
