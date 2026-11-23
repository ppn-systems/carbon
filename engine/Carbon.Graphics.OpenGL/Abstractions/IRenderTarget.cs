using System;

namespace Carbon.Graphics.OpenGL.Abstractions;

/// <summary>
/// Defines the contract for an off-screen framebuffer render target.
/// </summary>
public interface IRenderTarget : IDisposable
{
    /// <summary>
    /// Gets the OpenGL framebuffer object (FBO) handle.
    /// </summary>
    uint FramebufferHandle { get; }

    /// <summary>
    /// Gets the color texture backing this render target.
    /// </summary>
    ITexture ColorTexture { get; }

    /// <summary>
    /// Gets the width of the render target in pixels.
    /// </summary>
    uint Width { get; }

    /// <summary>
    /// Gets the height of the render target in pixels.
    /// </summary>
    uint Height { get; }

    /// <summary>
    /// Binds this render target as the active framebuffer.
    /// </summary>
    void Bind();

    /// <summary>
    /// Unbinds this render target and restores the default framebuffer.
    /// </summary>
    void Unbind();
}
