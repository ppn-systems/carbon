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

    /// <summary>Gets the minimum MMORPG progression zone tier (<c>0..3</c>) required for this landmark.</summary>
    public byte MinZoneTier { get; init; }

    /// <summary>Gets the maximum MMORPG progression zone tier (<c>0..3</c>) allowed for this landmark.</summary>
    public byte MaxZoneTier { get; init; } = 3;

    /// <summary>Returns the score of a candidate spot, or <c>null</c> when the rule does not accept it.</summary>
    public float? Evaluate(in MapTile tile, float river, bool flat, bool nearSea) =>
        this.Evaluate(in tile, river, flat, nearSea, zoneTier: 1);

    /// <summary>Returns the score of a candidate spot at <paramref name="zoneTier"/>, or <c>null</c> when rejected.</summary>
    public float? Evaluate(in MapTile tile, float river, bool flat, bool nearSea, byte zoneTier)
    {
        if (zoneTier < this.MinZoneTier || zoneTier > this.MaxZoneTier ||
            (this.RequireFlat && !flat) || (this.RequireNearSea && !nearSea) ||
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
        new(PointOfInterestType.SwampVillage, 26, 2.5f) { RequireFlat = true, HeightLevel = 0, RiverAbove = 8f, RiverAtMost = 18f, Biomes = [BiomeType.MistySwamp] },
        new(PointOfInterestType.DesertVillage, 26, 2.5f) { RequireFlat = true, HeightLevel = 0, RiverAbove = 8f, RiverAtMost = 18f, Biomes = [BiomeType.DuneDesert, BiomeType.RedMesa] },
        new(PointOfInterestType.SnowVillage, 26, 2.5f) { RequireFlat = true, HeightLevel = 0, RiverAbove = 8f, RiverAtMost = 18f, Biomes = [BiomeType.FrozenTundra] },

        // Village: flat fertile lowland, near water but outside the flood zone.
        new(PointOfInterestType.RiverVillage, 26, 2.5f)
        {
            RequireFlat = true,
            HeightLevel = 0,
            RiverAbove = 8f,
            RiverAtMost = 18f,
            Biomes = [BiomeType.LushPlains, BiomeType.Savanna, BiomeType.BlossomWoods, BiomeType.AncientForest],
            RiverSweetSpot = 12f,
            RiverSlope = 0.1f,
        },

        // Mine: a flat ledge on a plateau in rock country, away from rivers.
        new(PointOfInterestType.MineEntrance, 6, 1.2f)
        {
            RequireFlat = true,
            HeightLevel = 1,
            RiverAbove = 6f,
            Biomes = [BiomeType.AlpinePeaks, BiomeType.RedMesa, BiomeType.VolcanicAshland, BiomeType.FrozenTundra, BiomeType.Savanna],
        },

        // Camp: dry shore right next to a lake or a large river.
        new(PointOfInterestType.LakesideCamp, 4, 1.5f) { RequireFlat = true, RiverAbove = 2.4f, RiverAtMost = 4.5f, ExcludedBiomes = [BiomeType.MistySwamp] },

        // Shrine: only on high summits, scored below any settlement, so it is the fallback of mountain sites.
        new(PointOfInterestType.MountainShrine, 4, 0.5f) { RequireFlat = true, HeightLevel = 2, MinElevation = 0.78f, ScorePerElevation = 1f },

        // Boss Arena: endgame open ring in outer zones away from rivers.
        new(PointOfInterestType.BossArena, 14, 2.1f)
        {
            RequireFlat = true,
            RiverAbove = 8f,
            MinZoneTier = 2,
            ExcludedBiomes = [BiomeType.MistySwamp],
        },

        // Outlaw Bandit Camp: inland frontier camp away from river towns.
        new(PointOfInterestType.BanditCamp, 9, 1.8f)
        {
            RequireFlat = true,
            HeightLevel = 0,
            RiverAbove = 9f,
            MinZoneTier = 1,
        },

        // Ancient Ruins: overgrown monoliths in deep wilds and forests.
        new(PointOfInterestType.AncientRuins, 10, 1.7f)
        {
            RequireFlat = true,
            RiverAbove = 11f,
            Biomes = [BiomeType.AncientForest, BiomeType.DuneDesert, BiomeType.MistySwamp, BiomeType.TropicalJungle, BiomeType.BlossomWoods],
        },

        // Frontier Watchtower: roadside lookout in inland plains, mesas, or tundra.
        new(PointOfInterestType.Watchtower, 6, 1.4f)
        {
            RequireFlat = true,
            RiverAbove = 8f,
            Biomes = [BiomeType.LushPlains, BiomeType.Savanna, BiomeType.RedMesa, BiomeType.FrozenTundra],
        },
    ];

    /// <summary>Finds the first rule accepting a spot.</summary>
    public static (PointOfInterestType Type, float Score, int Radius) Evaluate(in MapTile tile, float river, bool flat, bool nearSea) =>
        Evaluate(in tile, river, flat, nearSea, zoneTier: 1);

    /// <summary>Finds the first rule accepting a spot within <paramref name="zoneTier"/>.</summary>
    public static (PointOfInterestType Type, float Score, int Radius) Evaluate(in MapTile tile, float river, bool flat, bool nearSea, byte zoneTier)
    {
        foreach (LandmarkRule rule in All)
        {
            if (rule.Evaluate(tile, river, flat, nearSea, zoneTier) is float score)
            {
                return (rule.Type, score, rule.Radius);
            }
        }

        return (PointOfInterestType.None, 0f, 0);
    }
}
