using Silk.NET.OpenGL;

namespace Carbon.Graphics.OpenGL.States;

/// <summary>
/// Represents the depth buffer configuration for 2D and 2.5D rendering pipelines.
/// </summary>
/// <param name="DepthTestEnabled">Whether depth testing is enabled.</param>
/// <param name="DepthWriteEnabled">Whether writing to the depth buffer is enabled.</param>
/// <param name="DepthFunction">The comparison function used to accept or reject incoming fragments.</param>
public readonly record struct DepthStencilState(
    bool DepthTestEnabled,
    bool DepthWriteEnabled,
    DepthFunction DepthFunction = DepthFunction.Lequal)
{
    /// <summary>
    /// No depth testing and no depth writing (standard for flat 2D overlays and UI).
    /// </summary>
    public static readonly DepthStencilState None = new(
        DepthTestEnabled: false,
        DepthWriteEnabled: false,
        DepthFunction: DepthFunction.Always);

    /// <summary>
    /// Depth testing enabled with depth write disabled (common for transparent or alpha-tested 2.5D sprites).
    /// </summary>
    public static readonly DepthStencilState DepthRead = new(
        DepthTestEnabled: true,
        DepthWriteEnabled: false,
        DepthFunction: DepthFunction.Lequal);

    /// <summary>
    /// Depth testing enabled with depth writing enabled (standard for solid terrain, walls, and 2.5D height geometries).
    /// </summary>
    public static readonly DepthStencilState DepthReadWrite = new(
        DepthTestEnabled: true,
        DepthWriteEnabled: true,
        DepthFunction: DepthFunction.Lequal);
}
