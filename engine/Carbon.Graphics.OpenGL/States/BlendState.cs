using Silk.NET.OpenGL;

namespace Carbon.Graphics.OpenGL.States;

/// <summary>
/// Represents the color blending configuration for the graphics pipeline.
/// </summary>
/// <param name="ColorSource">The blending factor for the source RGB channels.</param>
/// <param name="ColorDestination">The blending factor for the destination RGB channels.</param>
/// <param name="AlphaSource">The blending factor for the source Alpha channel.</param>
/// <param name="AlphaDestination">The blending factor for the destination Alpha channel.</param>
/// <param name="ColorEquation">The blend equation mode for RGB channels.</param>
/// <param name="AlphaEquation">The blend equation mode for Alpha channel.</param>
public readonly record struct BlendState(
    BlendingFactor ColorSource,
    BlendingFactor ColorDestination,
    BlendingFactor AlphaSource,
    BlendingFactor AlphaDestination,
    BlendEquationModeEXT ColorEquation = BlendEquationModeEXT.FuncAdd,
    BlendEquationModeEXT AlphaEquation = BlendEquationModeEXT.FuncAdd)
{
    /// <summary>
    /// Standard alpha blending state with pre-multiplied alpha.
    /// </summary>
    public static readonly BlendState AlphaBlend = new(
        BlendingFactor.One,
        BlendingFactor.OneMinusSrcAlpha,
        BlendingFactor.One,
        BlendingFactor.OneMinusSrcAlpha);

    /// <summary>
    /// Standard non-premultiplied alpha blending state (common for 2D/2.5D sprite graphics).
    /// </summary>
    public static readonly BlendState NonPremultiplied = new(
        BlendingFactor.SrcAlpha,
        BlendingFactor.OneMinusSrcAlpha,
        BlendingFactor.SrcAlpha,
        BlendingFactor.OneMinusSrcAlpha);

    /// <summary>
    /// Additive blending state (common for glowing particles, lights, and magical effects in 2.5D).
    /// </summary>
    public static readonly BlendState Additive = new(
        BlendingFactor.SrcAlpha,
        BlendingFactor.One,
        BlendingFactor.SrcAlpha,
        BlendingFactor.One);

    /// <summary>
    /// Opaque blending state where source color completely replaces destination.
    /// </summary>
    public static readonly BlendState Opaque = new(
        BlendingFactor.One,
        BlendingFactor.Zero,
        BlendingFactor.One,
        BlendingFactor.Zero);
}
