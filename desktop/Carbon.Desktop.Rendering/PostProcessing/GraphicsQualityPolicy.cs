// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Desktop.Rendering.PostProcessing;

/// <summary>
/// Parses the <c>graphics_quality</c> setting and picks the default preset from the GPU the driver reports (ADR-041).
/// </summary>
public static class GraphicsQualityPolicy
{
    // Software rasterizers: every offscreen pass costs CPU fill rate, so they get Low.
    private static readonly string[] s_softwareRenderers = ["llvmpipe", "softpipe", "swiftshader", "microsoft basic render", "gdi generic", "software rasterizer"];

    // Integrated GPUs: shared memory bandwidth, so they get Medium (light map and grading, no bloom taps).
    private static readonly string[] s_integratedRenderers = ["intel", "uhd graphics", "iris", "radeon(tm) graphics", "radeon graphics", "vega", "mali", "adreno", "powervr", "apple m"];

    /// <summary>Parses a setting value (<c>low</c>, <c>medium</c>, <c>high</c>, case-insensitive).</summary>
    /// <param name="value">The setting text.</param>
    /// <param name="quality">The parsed preset.</param>
    /// <returns><see langword="true"/> for a known preset; <see langword="false"/> for <c>auto</c>, empty or unknown text.</returns>
    public static bool TryParse(string? value, out GraphicsQuality quality)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "low":
                quality = GraphicsQuality.Low;
                return true;
            case "medium":
                quality = GraphicsQuality.Medium;
                return true;
            case "high":
                quality = GraphicsQuality.High;
                return true;
            default:
                quality = GraphicsQuality.Low;
                return false;
        }
    }

    /// <summary>Gets the setting text for a preset.</summary>
    /// <param name="quality">The preset.</param>
    /// <returns><c>low</c>, <c>medium</c> or <c>high</c>.</returns>
    public static string ToSettingValue(GraphicsQuality quality) => quality switch
    {
        GraphicsQuality.High => "high",
        GraphicsQuality.Medium => "medium",
        GraphicsQuality.Low or _ => "low",
    };

    /// <summary>Gets the default preset for the GPU named by the driver's <c>GL_RENDERER</c> string.</summary>
    /// <param name="renderer">The renderer string (e.g. <c>llvmpipe (LLVM 19.1.1, 256 bits)</c>).</param>
    /// <returns>Low for software rasterizers, Medium for integrated GPUs, High otherwise.</returns>
    public static GraphicsQuality DefaultFor(string? renderer)
    {
        if (string.IsNullOrWhiteSpace(renderer))
        {
            return GraphicsQuality.Medium;
        }

        if (ContainsAny(renderer, s_softwareRenderers))
        {
            return GraphicsQuality.Low;
        }

        return ContainsAny(renderer, s_integratedRenderers) ? GraphicsQuality.Medium : GraphicsQuality.High;
    }

    /// <summary>Resolves the preset to use: an explicit override, else the saved setting, else the GPU default.</summary>
    /// <param name="overrideValue">The <c>CARBON_GRAPHICS_QUALITY</c> value, if any.</param>
    /// <param name="saved">The <c>[display] graphics_quality</c> setting (<see langword="null"/> = auto).</param>
    /// <param name="renderer">The driver's renderer string.</param>
    /// <returns>The preset.</returns>
    public static GraphicsQuality Resolve(string? overrideValue, GraphicsQuality? saved, string? renderer) =>
        TryParse(overrideValue, out GraphicsQuality forced) ? forced : saved ?? DefaultFor(renderer);

    private static bool ContainsAny(string text, string[] needles)
    {
        for (int i = 0; i < needles.Length; i++)
        {
            if (text.Contains(needles[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
