// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Graphics.Batching.Common;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Minimap tile colours: the biome's <c>minimapColor</c> from its <c>.cvg.json</c>, shaded by terrain height (higher
/// is lighter, a cliff edge facing north casts a dark line), with roads and bridges drawn over it.
/// </summary>
internal static class MinimapPalette
{
    private static readonly ColorRgba s_road = new(200, 168, 120, 255);
    private static readonly ColorRgba s_bridge = new(138, 106, 66, 255);

    /// <summary>Returns the biome base colour (the art library's, or a built-in fallback when art is missing).</summary>
    internal static ColorRgba GetBiomeColor(BiomeType biome, IVectorArtRepository? art)
    {
        if (art is not null && art.TryGetBiome((byte)biome, out CompiledBiomeArt? biomeArt))
        {
            return biomeArt.MinimapColor;
        }

        return biome switch
        {
            BiomeType.DeepOcean => new ColorRgba(22, 58, 110, 255),
            BiomeType.ShallowWater => new ColorRgba(46, 125, 192, 255),
            BiomeType.BeachSand => new ColorRgba(234, 217, 164, 255),
            BiomeType.AncientForest => new ColorRgba(42, 107, 56, 255),
            BiomeType.MistySwamp => new ColorRgba(86, 105, 74, 255),
            BiomeType.DuneDesert => new ColorRgba(221, 174, 94, 255),
            BiomeType.Savanna => new ColorRgba(185, 169, 86, 255),
            BiomeType.RedMesa => new ColorRgba(180, 90, 56, 255),
            BiomeType.TropicalJungle => new ColorRgba(30, 140, 90, 255),
            BiomeType.FrozenTundra => new ColorRgba(211, 225, 236, 255),
            BiomeType.AlpinePeaks => new ColorRgba(154, 163, 174, 255),
            BiomeType.VolcanicAshland => new ColorRgba(78, 66, 68, 255),
            BiomeType.BlossomWoods => new ColorRgba(180, 139, 164, 255),
            BiomeType.LushPlains => new ColorRgba(95, 160, 78, 255),
            _ => new ColorRgba(95, 160, 78, 255),
        };
    }

    /// <summary>Returns the shaded colour of one tile.</summary>
    /// <param name="tile">The tile.</param>
    /// <param name="northHeightLevel">Height level of the tile to the north (same as the tile when unknown).</param>
    /// <param name="isRoad">Whether a road crosses the tile.</param>
    /// <param name="isBridge">Whether a bridge crosses the tile.</param>
    /// <param name="art">Art library for biome colours.</param>
    internal static ColorRgba GetTileColor(in MapTile tile, byte northHeightLevel, bool isRoad, bool isBridge, IVectorArtRepository? art)
    {
        if (isBridge)
        {
            return s_bridge;
        }

        if (isRoad)
        {
            return s_road;
        }

        ColorRgba baseColor = GetBiomeColor(tile.Biome, art);
        float shade = 0.92f + (0.05f * Math.Min((int)tile.HeightLevel, 3));
        if (northHeightLevel < tile.HeightLevel)
        {
            shade *= 1.12f; // lit cliff top
        }
        else if (northHeightLevel > tile.HeightLevel)
        {
            shade *= 0.72f; // shadow under a cliff
        }

        return Scale(baseColor, shade);
    }

    private static ColorRgba Scale(ColorRgba color, float factor) =>
        new(
            (byte)Math.Clamp((int)MathF.Round(color.R * factor), 0, 255),
            (byte)Math.Clamp((int)MathF.Round(color.G * factor), 0, 255),
            (byte)Math.Clamp((int)MathF.Round(color.B * factor), 0, 255),
            255);
}
