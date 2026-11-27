// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;

namespace Carbon.Desktop.Rendering.PostProcessing;

/// <summary>
/// One colour grade for the composite pass (ADR-041): <c>c' = lift + c × (gain − lift)</c> per channel, then contrast
/// around mid grey, then saturation around luma, then a vignette that darkens the screen edges.
/// </summary>
/// <param name="Lift">Colour the blacks are lifted to (shadow tint).</param>
/// <param name="Gain">Multiplier for the whites (highlight tint).</param>
/// <param name="Contrast">Contrast around 0.5 (1 = unchanged).</param>
/// <param name="Saturation">Saturation around luma (1 = unchanged, 0 = grey).</param>
/// <param name="Vignette">Darkening at the screen corners, 0..1.</param>
public readonly record struct ColorGrade(Vector3 Lift, Vector3 Gain, float Contrast, float Saturation, float Vignette)
{
    /// <summary>Gets the grade that leaves every colour unchanged.</summary>
    public static ColorGrade Identity { get; } = new(Vector3.Zero, Vector3.One, 1f, 1f, 0f);

    /// <summary>Linearly blends two grades.</summary>
    /// <param name="a">Grade at <paramref name="t"/> = 0.</param>
    /// <param name="b">Grade at <paramref name="t"/> = 1.</param>
    /// <param name="t">Blend factor, 0..1.</param>
    /// <returns>The blended grade.</returns>
    public static ColorGrade Lerp(in ColorGrade a, in ColorGrade b, float t) => new(
        Vector3.Lerp(a.Lift, b.Lift, t),
        Vector3.Lerp(a.Gain, b.Gain, t),
        a.Contrast + ((b.Contrast - a.Contrast) * t),
        a.Saturation + ((b.Saturation - a.Saturation) * t),
        a.Vignette + ((b.Vignette - a.Vignette) * t));

    /// <summary>Applies the grade to a colour on the CPU (the shader's reference; vignette excluded).</summary>
    /// <param name="color">Linear 0..1 colour.</param>
    /// <returns>The graded colour, clamped to 0..1.</returns>
    public Vector3 Apply(Vector3 color)
    {
        Vector3 c = this.Lift + (color * (this.Gain - this.Lift));
        c = ((c - new Vector3(0.5f)) * this.Contrast) + new Vector3(0.5f);
        float luma = Vector3.Dot(c, new Vector3(0.2126f, 0.7152f, 0.0722f));
        c = new Vector3(luma) + ((c - new Vector3(luma)) * this.Saturation);
        return Vector3.Clamp(c, Vector3.Zero, Vector3.One);
    }
}
