// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Hydrology;
using Carbon.Maps.Noise;
using Carbon.Primitives.Hashing;
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

        float riverDist = _rivers.GetDistance(sample.WorldX, sample.WorldY, out bool isDeepChannel);
        sample.RiverDistance = riverDist;

        // Desert oasis springs in arid dunes: skip expensive 2-octave noise unless inside desert climate bounds.
        bool inOasisClimate = sample.Temperature > 0.68f && sample.RawMoisture < 0.28f &&
            sample.Elevation >= 0.42f && sample.Elevation <= 0.62f;
        float oasisNoise = inOasisClimate
            ? _lakes.Evaluate((sample.Fx * 3.6f) + 120f, (sample.Fy * 3.6f) + 120f, octaves: 2)
            : 0f;
        bool isOasis = inOasisClimate && oasisNoise > 0.86f;

        if (riverDist <= WorldConstants.RiverChannel || isOasis)
        {
            byte variant = (byte)(SpatialHash.Hash32(_seed, sample.WorldX, sample.WorldY, 0x0A71UL) & 3);
            bool deep = isDeepChannel && !isOasis;
            BiomeType waterBiome = deep ? BiomeType.DeepOcean : BiomeType.ShallowWater;
            sample.Tile = new MapTile(
                waterBiome,
                Elevation: deep ? 0.25f : 0.28f,
                sample.Temperature,
                Moisture: 0.85f,
                IsSolid: deep,
                variant,
                HeightLevel: TopographyHelper.ResolveTerrainHeightLevel(sample.Relief, sample.CoastElevation, waterBiome, isInlandWater: true));
            return false;
        }

        if (inOasisClimate && oasisNoise > 0.72f)
        {
            // Moist oasis halo nourishes date/jungle palms around desert pools.
            float halo = Math.Clamp((oasisNoise - 0.72f) / 0.14f, 0f, 1f);
            sample.Moisture = Math.Clamp(sample.Moisture + (0.45f * halo), 0f, 1f);
        }

        if (riverDist < WorldConstants.RiparianReach)
        {
            // Riparian zone: ease ground toward the water and wet it; preserve lush forest/plateau banks
            // by clamping sand-level lowering to coasts and arid climates rather than turning forests into beach sand.
            float k = 1f - Math.Clamp((riverDist - WorldConstants.RiverChannel) / (WorldConstants.RiparianReach - WorldConstants.RiverChannel), 0f, 1f);
            k = k * k * (3f - (2f * k));
            bool sandyBank = sample.CoastElevation < WorldConstants.CoastLevel || (sample.Temperature > 0.62f && sample.RawMoisture < 0.35f);
            float floorElev = sandyBank ? 0.36f : 0.385f;
            float elevation = MathF.Max(sample.Elevation - (0.08f * k), MathF.Min(sample.Elevation, floorElev));
            if (riverDist < 3.0f && sandyBank)
            {
                elevation = MathF.Min(elevation, 0.37f);
            }

            sample.Elevation = elevation;
            sample.Moisture = Math.Clamp(sample.Moisture + (0.30f * k), 0f, 1f);
        }

        return true;
    }
}
