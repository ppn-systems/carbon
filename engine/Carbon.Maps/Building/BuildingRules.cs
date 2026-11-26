// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Maps.Building;

/// <summary>
/// Global construction tuning shared by every structure definition.
/// </summary>
public sealed record BuildingRules
{
    /// <summary>Gets the rules used when <c>structures.json</c> does not override them.</summary>
    public static BuildingRules Default { get; } = new();

    /// <summary>Gets the fraction of each material refunded when a completed structure is deconstructed.</summary>
    public float DeconstructRefundFraction { get; init; } = 0.75f;

    /// <summary>Gets the fraction of each material dropped when a structure is destroyed by damage.</summary>
    public float DestroyedDropFraction { get; init; } = 0.25f;

    /// <summary>Gets the fraction of the construction work needed to deconstruct a structure.</summary>
    public float DeconstructWorkFraction { get; init; } = 0.5f;

    /// <summary>Gets the max Chebyshev distance (tiles) between a builder and the footprint to deliver or work.</summary>
    public int BuilderReachTiles { get; init; } = 2;

    /// <summary>Gets the max Chebyshev distance (tiles) between a player and the footprint to place a blueprint.</summary>
    public int PlacementReachTiles { get; init; } = 12;
}
