// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Desktop.Rendering.PostProcessing;

/// <summary>
/// Parses the <c>graphics_quality</c> setting and picks the default preset from the GPU the driver reports (ADR-041).
/// </summary>
public static class GraphicsQualityPolicy
{
    // Words of GL_RENDERER that name a software rasterizer: every offscreen pass costs CPU fill rate, so Low.
    // ("Microsoft Basic Render Driver" and "GDI Generic" are the Windows fallbacks.)
    private static readonly string[] s_softwareWords = ["llvmpipe", "softpipe", "swiftshader", "basic", "generic"];

    // Words that name an integrated GPU (shared memory bandwidth), so Medium. AMD APUs report "Radeon Graphics"
    // (discrete cards report a model, "Radeon RX 6700"), handled separately.
    private static readonly string[] s_integratedWords = ["intel", "iris", "uhd", "vega", "mali", "adreno", "powervr", "apple"];
    private static readonly string[] s_radeonWord = ["radeon"];
    private static readonly string[] s_graphicsWord = ["graphics"];

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

        if (HasWord(renderer, s_softwareWords))
        {
            return GraphicsQuality.Low;
        }

        bool amdApu = HasWord(renderer, s_radeonWord) && HasWord(renderer, s_graphicsWord);
        return amdApu || HasWord(renderer, s_integratedWords) ? GraphicsQuality.Medium : GraphicsQuality.High;
    }

    /// <summary>Resolves the preset to use: an explicit override, else the saved setting, else the GPU default.</summary>
    /// <param name="overrideValue">The <c>CARBON_GRAPHICS_QUALITY</c> value, if any.</param>
    /// <param name="saved">The <c>[display] graphics_quality</c> setting (<see langword="null"/> = auto).</param>
    /// <param name="renderer">The driver's renderer string.</param>
    /// <returns>The preset.</returns>
    public static GraphicsQuality Resolve(string? overrideValue, GraphicsQuality? saved, string? renderer) =>
        TryParse(overrideValue, out GraphicsQuality forced) ? forced : saved ?? DefaultFor(renderer);

    // Whether any of the renderer string's words (runs of letters and digits, case-insensitive) is in the list.
    private static bool HasWord(string renderer, string[] words)
    {
        ReadOnlySpan<char> text = renderer;
        int start = -1;
        for (int i = 0; i <= text.Length; i++)
        {
            bool letter = i < text.Length && char.IsLetterOrDigit(text[i]);
            if (letter && start < 0)
            {
                start = i;
            }
            else if (!letter && start >= 0)
            {
                ReadOnlySpan<char> word = text[start..i];
                for (int w = 0; w < words.Length; w++)
                {
                    if (word.Equals(words[w], StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                start = -1;
            }
        }

        return false;
    }
}
