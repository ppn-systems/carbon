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

        // The sea: rivers discharge into it and it is never carved. Its depth grows with distance from the coast,
        // so it falls away smoothly from the beach to the abyss (ADR-032).
        if (sample.Elevation < WorldConstants.SeaLevel)
        {
            float seaDepth = WaterDepth.SeaDepthAtCoastDistance(_rivers.GetCoastDistance(sample.WorldX, sample.WorldY));
            this.SetWater(ref sample, seaDepth, heightLevel: 0);
            return false;
        }

        float riverDist = _rivers.GetDistance(sample.WorldX, sample.WorldY, out float riverDepth);
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
            // Oasis pools are a small spring, never deeper than a wade, and shoal to nothing where the desert
            // climate that holds them ends (otherwise the pool is cut off at full depth along that line).
            float oasisDepth = 0f;
            if (isOasis)
            {
                float edge = MathF.Min(
                    MathF.Min(sample.Temperature - 0.68f, 0.28f - sample.RawMoisture),
                    MathF.Min(sample.Elevation - 0.42f, 0.62f - sample.Elevation));
                float fade = Math.Clamp(edge / 0.02f, 0f, 1f);
                oasisDepth = WaterDepth.MinDepth + ((oasisNoise - 0.86f) / 0.14f * (WaterDepth.MediumFrom - WaterDepth.MinDepth) * 0.9f * fade);
            }

            float depth = riverDist <= WorldConstants.RiverChannel ? MathF.Max(riverDepth, oasisDepth) : oasisDepth;
            byte heightLevel = TopographyHelper.ResolveTerrainHeightLevel(sample.Relief, sample.CoastElevation, BiomeType.ShallowWater, isInlandWater: true);
            this.SetWater(ref sample, depth, heightLevel);
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

    private void SetWater(ref TileSample sample, float depth, byte heightLevel)
    {
        byte variant = (byte)(SpatialHash.Hash32(_seed, sample.WorldX, sample.WorldY, 0x0A71UL) & 3);
        BiomeType biome = WaterDepth.BiomeFor(WaterDepth.TierOf(depth));
        sample.Tile = new MapTile(
            biome,
            WaterDepth.ToElevation(depth),
            sample.Temperature,
            Moisture: 0.85f,
            BiomeProperties.Get(biome).IsSolid,
            variant,
            heightLevel);
    }
}
