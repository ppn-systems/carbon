// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Batching.Common;

namespace Carbon.Desktop.Rendering.VectorArt;

/// <summary>
/// Pre-compiled biome visual configuration and surface micro-detail decals loaded from <c>biomes/*.cvg.json</c>.
/// </summary>
public sealed class CompiledBiomeArt
{
    /// <summary>Initializes a compiled biome blueprint.</summary>
    public CompiledBiomeArt(
        byte biomeId,
        string id,
        string displayName,
        ColorRgba groundColor,
        ColorRgba minimapColor,
        ColorRgba cliffBase,
        ColorRgba cliffStrata,
        ColorRgba cliffHighlight,
        ColorRgba cliffShadow,
        int detailChancePercent,
        CompiledVectorShape[][] detailVariants,
        BiomeSurfaceStyle? surface = null,
        BiomeDecalLayer[]? decalLayers = null)
    {
        this.BiomeId = biomeId;
        this.Id = id;
        this.DisplayName = displayName;
        this.GroundColor = groundColor;
        this.MinimapColor = minimapColor;
        this.CliffBase = cliffBase;
        this.CliffStrata = cliffStrata;
        this.CliffHighlight = cliffHighlight;
        this.CliffShadow = cliffShadow;
        this.DetailChancePercent = Math.Clamp(detailChancePercent, 0, 100);
        this.DetailVariants = detailVariants;
        this.Surface = surface ?? BiomeSurfaceStyle.FromGround(new Vector3(groundColor.R, groundColor.G, groundColor.B));
        this.DecalLayers = decalLayers ?? BiomeDecalLayer.None;
    }

    /// <summary>Numeric biome ID matching <c>BiomeType</c>.</summary>
    public byte BiomeId { get; }

    /// <summary>Canonical biome identifier.</summary>
    public string Id { get; }

    /// <summary>Human-readable biome display name.</summary>
    public string DisplayName { get; }

    /// <summary>Base tile ground color.</summary>
    public ColorRgba GroundColor { get; }

    /// <summary>Minimap radar swatch color.</summary>
    public ColorRgba MinimapColor { get; }

    /// <summary>Cliff wall primary face color.</summary>
    public ColorRgba CliffBase { get; }

    /// <summary>Cliff horizontal strata stripe color.</summary>
    public ColorRgba CliffStrata { get; }

    /// <summary>Cliff top-edge sunlit highlight color.</summary>
    public ColorRgba CliffHighlight { get; }

    /// <summary>Cliff base drop shadow color.</summary>
    public ColorRgba CliffShadow { get; }

    /// <summary>Percentage chance per tile to stamp a surface micro-detail decal.</summary>
    public int DetailChancePercent { get; }

    /// <summary>Array of pre-compiled tile micro-detail decal variants in <c>[0..32]</c> tile space.</summary>
    public CompiledVectorShape[][] DetailVariants { get; }

    /// <summary>Gets the ground colour field (the blueprint's <c>surface</c> block).</summary>
    public BiomeSurfaceStyle Surface { get; }

    /// <summary>Gets the scattered ground decal layers (the blueprint's <c>decals</c> list).</summary>
    public BiomeDecalLayer[] DecalLayers { get; }
}
