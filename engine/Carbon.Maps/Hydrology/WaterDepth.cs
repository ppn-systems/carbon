// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;

namespace Carbon.Maps.Hydrology;

/// <summary>
/// The continuous water depth field and the tiers cut from it (ADR-032). Depth is measured in elevation units
/// below the water surface and is stored in a water tile as <c>Elevation = SeaLevel - depth</c>, so chunk deltas
/// and every existing reader of <see cref="MapTile.Elevation"/> keep working.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The sea gets deeper with distance from the coast (<see cref="SeaDepthAtCoastDistance"/>), so it falls
/// away smoothly from the beach to the abyss instead of following the terrain noise.</item>
/// <item>Rivers and lakes are as deep as they are wide (<see cref="MaxDepthForChannelWidth"/>): a narrow stream is
/// never deep or black.</item>
/// </list>
/// </remarks>
public static class WaterDepth
{
    /// <summary>The shallowest a water tile can be.</summary>
    public const float MinDepth = 0.004f;

    /// <summary>The deepest a water tile can be (the abyss floor).</summary>
    public const float MaxDepth = WorldConstants.SeaLevel - 0.02f;

    /// <summary>Depth from which water is <see cref="WaterDepthTier.Shallow"/>.</summary>
    public const float ShallowFrom = 0.015f;

    /// <summary>Depth from which water is <see cref="WaterDepthTier.Medium"/> (swimming).</summary>
    public const float MediumFrom = 0.045f;

    /// <summary>Depth from which water is <see cref="WaterDepthTier.Deep"/> (diving). Matches the old deep-ocean line (elevation 0.22).</summary>
    public const float DeepFrom = 0.10f;

    /// <summary>Depth from which water is <see cref="WaterDepthTier.Abyss"/>.</summary>
    public const float AbyssFrom = 0.20f;

    /// <summary>Depth gained per tile of channel width: a river must be about 8 tiles wide to swim in and 18 to dive in.</summary>
    public const float DepthPerWidthTile = 0.0055f;

    /// <summary>The deepest a river or lake can be: inland water never reaches the abyss.</summary>
    public const float MaxInlandDepth = 0.16f;

    // The sea shelf: depth at increasing distances from the coast, in tiles (piecewise linear, strictly increasing).
    private static ReadOnlySpan<float> ShelfDistances => [0f, 4f, 14f, 40f, 120f, 220f];

    private static ReadOnlySpan<float> ShelfDepths => [MinDepth, ShallowFrom, MediumFrom, DeepFrom, AbyssFrom, MaxDepth];

    /// <summary>Gets the tier of a depth.</summary>
    /// <param name="depth">Depth below the surface; zero or less is dry land.</param>
    /// <returns>The tier.</returns>
    public static WaterDepthTier TierOf(float depth) => depth switch
    {
        <= 0f => WaterDepthTier.Dry,
        < ShallowFrom => WaterDepthTier.Shore,
        < MediumFrom => WaterDepthTier.Shallow,
        < DeepFrom => WaterDepthTier.Medium,
        < AbyssFrom => WaterDepthTier.Deep,
        _ => WaterDepthTier.Abyss,
    };

    /// <summary>Gets the depth of a tile: zero on land.</summary>
    /// <param name="tile">The tile.</param>
    /// <returns>The depth below the surface.</returns>
    public static float Of(in MapTile tile) =>
        WaterWading.IsWater(tile.Biome) ? Math.Clamp(WorldConstants.SeaLevel - tile.Elevation, MinDepth, MaxDepth) : 0f;

    /// <summary>Gets the depth tier of a tile.</summary>
    /// <param name="tile">The tile.</param>
    /// <returns>The tier; <see cref="WaterDepthTier.Dry"/> on land.</returns>
    public static WaterDepthTier TierOf(in MapTile tile) => TierOf(Of(tile));

    /// <summary>Gets the elevation a water tile of this depth stores.</summary>
    /// <param name="depth">The depth.</param>
    /// <returns>The tile elevation.</returns>
    public static float ToElevation(float depth) => WorldConstants.SeaLevel - Math.Clamp(depth, MinDepth, MaxDepth);

    /// <summary>
    /// Gets the biome a water tier is drawn and collided as: diving water is <see cref="BiomeType.DeepOcean"/>,
    /// everything shallower is <see cref="BiomeType.ShallowWater"/>.
    /// </summary>
    /// <param name="tier">A water tier.</param>
    /// <returns>The biome.</returns>
    public static BiomeType BiomeFor(WaterDepthTier tier) => tier >= WaterDepthTier.Deep ? BiomeType.DeepOcean : BiomeType.ShallowWater;

    /// <summary>Gets the deepest a river or lake of this width may be.</summary>
    /// <param name="widthTiles">Full channel width in tiles.</param>
    /// <returns>The depth cap.</returns>
    public static float MaxDepthForChannelWidth(float widthTiles) =>
        Math.Clamp(widthTiles * DepthPerWidthTile, MinDepth, MaxInlandDepth);

    /// <summary>Gets the sea depth at a distance from the coast: strictly increasing, from the waterline to the abyss floor.</summary>
    /// <param name="tiles">Distance to the nearest land, in tiles.</param>
    /// <returns>The depth.</returns>
    public static float SeaDepthAtCoastDistance(float tiles)
    {
        ReadOnlySpan<float> distances = ShelfDistances;
        ReadOnlySpan<float> depths = ShelfDepths;
        if (!(tiles > 0f))
        {
            return depths[0];
        }

        for (int i = 1; i < distances.Length; i++)
        {
            if (tiles < distances[i])
            {
                float t = (tiles - distances[i - 1]) / (distances[i] - distances[i - 1]);
                return depths[i - 1] + ((depths[i] - depths[i - 1]) * t);
            }
        }

        return depths[^1];
    }
}
