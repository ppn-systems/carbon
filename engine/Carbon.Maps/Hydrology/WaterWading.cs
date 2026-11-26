// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Maps.Biomes;

namespace Carbon.Maps.Hydrology;

/// <summary>
/// Deterministic, server-authoritative rules for wading through shallow water.
/// </summary>
/// <remarks>
/// Water depth is a normalized value in [0, 1]: 0 on dry land, just above 0 at the waterline and 1 at the
/// deepest wadeable point (two tiles from the shore, or against the edge of deep water). It is derived from
/// the distance between a position and the nearest dry tile and the nearest deep-water tile, so it changes
/// smoothly inside a tile instead of stepping at tile borders. Deep water itself stays impassable (solid).
/// </remarks>
public static class WaterWading
{
    /// <summary>Distance from the shore, in tiles, at which shallow water reaches full wading depth.</summary>
    public const float ShoreRampTiles = 2f;

    /// <summary>Distance from deep water, in tiles, over which shallow water deepens towards full depth.</summary>
    public const float DeepRampTiles = 2f;

    /// <summary>Movement speed multiplier at the waterline (ankle-deep).</summary>
    public const float ShoreSpeedMultiplier = 0.75f;

    /// <summary>Movement speed multiplier at full wading depth (waist-deep).</summary>
    public const float DeepSpeedMultiplier = 0.40f;

    /// <summary>
    /// Gets whether <paramref name="biome"/> is water of any depth.
    /// </summary>
    public static bool IsWater(BiomeType biome) => biome is BiomeType.ShallowWater or BiomeType.DeepOcean;

    /// <summary>
    /// Computes the normalized wading depth from the distances to the nearest dry tile and the nearest deep tile.
    /// </summary>
    /// <param name="distanceToDry">Distance in world units to the nearest dry (non-water) tile; any value at or above the shore ramp means "far".</param>
    /// <param name="distanceToDeep">Distance in world units to the nearest deep-water tile; any value at or above the deep ramp means "far".</param>
    /// <param name="tileSize">Tile edge length in world units.</param>
    /// <returns>Depth in [0, 1].</returns>
    public static float DepthFromDistances(float distanceToDry, float distanceToDeep, float tileSize)
    {
        float shore = Math.Clamp(distanceToDry / (ShoreRampTiles * tileSize), 0f, 1f);
        float deep = 1f - Math.Clamp(distanceToDeep / (DeepRampTiles * tileSize), 0f, 1f);
        float depth = MathF.Max(shore, deep);

        // Smoothstep: the shelf drops gently at the waterline and flattens out at full depth.
        return depth * depth * (3f - (2f * depth));
    }

    /// <summary>
    /// Gets the movement speed multiplier for wading at <paramref name="depth"/>.
    /// </summary>
    /// <param name="depth">Normalized wading depth in [0, 1].</param>
    /// <returns>A multiplier between <see cref="DeepSpeedMultiplier"/> and <see cref="ShoreSpeedMultiplier"/>.</returns>
    public static float SpeedMultiplier(float depth)
    {
        float d = Math.Clamp(depth, 0f, 1f);
        return ShoreSpeedMultiplier + ((DeepSpeedMultiplier - ShoreSpeedMultiplier) * d);
    }
}
