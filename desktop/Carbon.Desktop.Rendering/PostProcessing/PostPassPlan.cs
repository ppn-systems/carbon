// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Desktop.Rendering.PostProcessing;

/// <summary>
/// Which world passes a <see cref="GraphicsQuality"/> preset runs (ADR-041). The renderer reads only this, so turning
/// a preset down is guaranteed to switch the passes off.
/// </summary>
/// <param name="UsesOffscreenScene">The world renders into an offscreen target that the composite pass reads.</param>
/// <param name="LightMapDivisor">The light map is the viewport size divided by this (0 = no light map).</param>
/// <param name="ColorGrading">The composite applies the time-of-day grade.</param>
/// <param name="Vignette">The composite darkens the screen edges.</param>
/// <param name="Bloom">The composite adds a wide blur of the light map's overbright part.</param>
public readonly record struct PostPassPlan(bool UsesOffscreenScene, int LightMapDivisor, bool ColorGrading, bool Vignette, bool Bloom)
{
    /// <summary>Gets whether a light map is rendered.</summary>
    public bool UsesLightMap => this.LightMapDivisor > 0;

    /// <summary>Gets the plan for <paramref name="quality"/>.</summary>
    /// <param name="quality">The preset.</param>
    /// <returns>The passes to run.</returns>
    public static PostPassPlan For(GraphicsQuality quality) => quality switch
    {
        GraphicsQuality.High => new PostPassPlan(UsesOffscreenScene: true, LightMapDivisor: 2, ColorGrading: true, Vignette: true, Bloom: true),
        GraphicsQuality.Medium => new PostPassPlan(UsesOffscreenScene: true, LightMapDivisor: 2, ColorGrading: true, Vignette: true, Bloom: false),
        GraphicsQuality.Low or _ => new PostPassPlan(UsesOffscreenScene: false, LightMapDivisor: 0, ColorGrading: false, Vignette: false, Bloom: false),
    };

    /// <summary>Gets the light map size for a viewport (at least 1 × 1), or zero when there is no light map.</summary>
    /// <param name="viewportWidth">Viewport width in pixels.</param>
    /// <param name="viewportHeight">Viewport height in pixels.</param>
    /// <returns>The light map size.</returns>
    public (uint Width, uint Height) LightMapSize(int viewportWidth, int viewportHeight) => this.UsesLightMap
        ? ((uint)System.Math.Max(1, viewportWidth / this.LightMapDivisor), (uint)System.Math.Max(1, viewportHeight / this.LightMapDivisor))
        : (0u, 0u);
}
