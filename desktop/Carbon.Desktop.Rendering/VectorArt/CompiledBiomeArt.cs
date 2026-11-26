// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
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
        CompiledVectorShape[][] detailVariants)
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
}
