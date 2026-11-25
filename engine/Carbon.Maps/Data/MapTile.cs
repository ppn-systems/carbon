// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Maps.Biomes;

namespace Carbon.Maps.Data;

/// <summary>
/// Unmanaged 2D map tile containing biome classification, climate data, and physical properties.
/// </summary>
public readonly record struct MapTile(
    BiomeType Biome,
    float Elevation,
    float Temperature,
    float Moisture,
    bool IsSolid,
    byte Variant,
    byte HeightLevel = 0)
{
    /// <summary>
    /// Gets the visual RGBA hex color for zero-asset procedural rendering.
    /// </summary>
    public uint ColorRgba => BiomeProperties.Get(this.Biome).ColorRgba;

    /// <summary>
    /// Gets the entity movement speed multiplier across this terrain tile.
    /// </summary>
    public float MoveSpeedMultiplier => BiomeProperties.Get(this.Biome).MoveSpeedMultiplier;
}
