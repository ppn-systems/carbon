// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;

namespace Carbon.Maps.Landmarks;

/// <summary>
/// Where a kind of point of interest may be founded and how much a candidate spot is worth. Rules are tried
/// in order and the first that accepts a spot scores it; the best-scoring spot of a site wins.
/// </summary>
/// <param name="Type">The point of interest it founds.</param>
/// <param name="Radius">Footprint radius in tiles.</param>
/// <param name="Score">Base score.</param>
public sealed record LandmarkRule(PointOfInterestType Type, int Radius, float Score)
{
    /// <summary>Gets a value indicating whether the 9x9 area must be flat, dry and at one height level.</summary>
    public bool RequireFlat { get; init; }

    /// <summary>Gets a value indicating whether open sea must lie about 10 tiles away.</summary>
    public bool RequireNearSea { get; init; }

    /// <summary>Gets the required height level, or <c>null</c> for any.</summary>
    public byte? HeightLevel { get; init; }

    /// <summary>Gets the river distance the spot must exceed (exclusive).</summary>
    public float RiverAbove { get; init; } = float.NegativeInfinity;

    /// <summary>Gets the river distance the spot may not exceed (inclusive).</summary>
    public float RiverAtMost { get; init; } = float.PositiveInfinity;

    /// <summary>Gets the allowed biomes; empty for any.</summary>
    public BiomeType[] Biomes { get; init; } = [];

    /// <summary>Gets the forbidden biomes.</summary>
    public BiomeType[] ExcludedBiomes { get; init; } = [];

    /// <summary>Gets the minimum terrain elevation.</summary>
    public float MinElevation { get; init; } = float.NegativeInfinity;

    /// <summary>Gets the score added per unit of elevation (summits rank higher).</summary>
    public float ScorePerElevation { get; init; }

    /// <summary>Gets the ideal river distance, or NaN; the score drops by <see cref="RiverSlope"/> per tile away from it.</summary>
    public float RiverSweetSpot { get; init; } = float.NaN;

    /// <summary>Gets the score lost per tile away from <see cref="RiverSweetSpot"/>.</summary>
    public float RiverSlope { get; init; }

    /// <summary>Returns the score of a candidate spot, or <c>null</c> when the rule does not accept it.</summary>
    /// <param name="tile">The spot's tile.</param>
    /// <param name="river">Width-scaled river distance.</param>
    /// <param name="flat">Whether the area is flat.</param>
    /// <param name="nearSea">Whether open sea is close.</param>
    /// <returns>The score, or <c>null</c>.</returns>
    public float? Evaluate(in MapTile tile, float river, bool flat, bool nearSea)
    {
        if ((this.RequireFlat && !flat) || (this.RequireNearSea && !nearSea) ||
            (this.HeightLevel is byte level && tile.HeightLevel != level) ||
            !(river > this.RiverAbove && river <= this.RiverAtMost) ||
            tile.Elevation < this.MinElevation ||
            (this.Biomes.Length > 0 && Array.IndexOf(this.Biomes, tile.Biome) < 0) ||
            Array.IndexOf(this.ExcludedBiomes, tile.Biome) >= 0)
        {
            return null;
        }

        float score = this.ScorePerElevation != 0f ? this.Score + (this.ScorePerElevation * tile.Elevation) : this.Score;
        if (!float.IsNaN(this.RiverSweetSpot))
        {
            score -= MathF.Abs(river - this.RiverSweetSpot) * this.RiverSlope;
        }

        return score;
    }
}

/// <summary>
/// The settlement rules of the world, in priority order. To add a kind of point of interest, add its rule
/// here and its planner in <see cref="LandmarkPlanners"/>.
/// </summary>
public static class LandmarkRules
{
    /// <summary>Gets the rules in priority order.</summary>
    public static LandmarkRule[] All { get; } =
    [
        // Harbour: coast beside a river mouth.
        new(PointOfInterestType.Harbor, 12, 3f) { RequireNearSea = true, RiverAtMost = 12f, HeightLevel = 0 },

        // Biome villages: each climate builds its own kind of settlement by the water.
        new(PointOfInterestType.SwampVillage, 16, 2.5f) { RequireFlat = true, HeightLevel = 0, RiverAbove = 3f, RiverAtMost = 11f, Biomes = [BiomeType.MistySwamp] },
        new(PointOfInterestType.DesertVillage, 16, 2.5f) { RequireFlat = true, HeightLevel = 0, RiverAbove = 3f, RiverAtMost = 11f, Biomes = [BiomeType.DuneDesert, BiomeType.RedMesa] },
        new(PointOfInterestType.SnowVillage, 16, 2.5f) { RequireFlat = true, HeightLevel = 0, RiverAbove = 3f, RiverAtMost = 11f, Biomes = [BiomeType.FrozenTundra] },

        // Village: flat fertile lowland, near water but outside the flood zone.
        new(PointOfInterestType.RiverVillage, 16, 2.5f)
        {
            RequireFlat = true,
            HeightLevel = 0,
            RiverAbove = 4.5f,
            RiverAtMost = 11f,
            Biomes = [BiomeType.LushPlains, BiomeType.Savanna, BiomeType.BlossomWoods, BiomeType.AncientForest],
            RiverSweetSpot = 7f,
            RiverSlope = 0.1f,
        },

        // Camp: dry shore right next to a lake or a large river.
        new(PointOfInterestType.LakesideCamp, 4, 1.5f) { RequireFlat = true, RiverAbove = 2.4f, RiverAtMost = 4.5f, ExcludedBiomes = [BiomeType.MistySwamp] },

        // Shrine: only on high summits, scored below any settlement, so it is the fallback of mountain sites.
        new(PointOfInterestType.MountainShrine, 4, 0.5f) { RequireFlat = true, HeightLevel = 2, MinElevation = 0.78f, ScorePerElevation = 1f },
    ];

    /// <summary>Finds the first rule accepting a spot.</summary>
    /// <param name="tile">The spot's tile.</param>
    /// <param name="river">Width-scaled river distance.</param>
    /// <param name="flat">Whether the area is flat.</param>
    /// <param name="nearSea">Whether open sea is close.</param>
    /// <returns>The point of interest type, its score and radius (<see cref="PointOfInterestType.None"/> when none).</returns>
    public static (PointOfInterestType Type, float Score, int Radius) Evaluate(in MapTile tile, float river, bool flat, bool nearSea)
    {
        foreach (LandmarkRule rule in All)
        {
            if (rule.Evaluate(tile, river, flat, nearSea) is float score)
            {
                return (rule.Type, score, rule.Radius);
            }
        }

        return (PointOfInterestType.None, 0f, 0);
    }
}
