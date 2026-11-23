// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.OpenGL.Abstractions;
using Carbon.Graphics.OpenGL.States;
using Silk.NET.OpenGL;

namespace Carbon.Graphics.OpenGL.Device;

/// <summary>
/// Primary hardware graphics device orchestrating the OpenGL pipeline, state caching, and draw commands.
/// </summary>
public sealed class GraphicsDevice : IGraphicsDevice
{
    private bool _disposed;

    /// <inheritdoc/>
    public GL Gl { get; }

    /// <summary>
    /// Gets the state cache manager eliminating redundant GPU driver calls.
    /// </summary>
    public GlStateManager StateManager { get; }

    /// <summary>
    /// Gets the queried hardware capabilities of this graphics device.
    /// </summary>
    public GraphicsDeviceCapabilities Capabilities { get; }

    /// <inheritdoc/>
    public bool IsActive => !_disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="GraphicsDevice"/> class wrapping a Silk.NET OpenGL instance.
    /// </summary>
    /// <param name="gl">The OpenGL API instance.</param>
    public GraphicsDevice(GL gl)
    {
        this.Gl = gl ?? throw new ArgumentNullException(nameof(gl));
        this.StateManager = new GlStateManager(gl);
        this.Capabilities = new GraphicsDeviceCapabilities(gl);
    }

    /// <inheritdoc/>
    public void SetViewport(int x, int y, uint width, uint height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        this.StateManager.SetViewport(x, y, width, height);
    }

    /// <inheritdoc/>
    public void SetClearColor(in Vector4 color)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        this.Gl.ClearColor(color.X, color.Y, color.Z, color.W);
    }

    /// <inheritdoc/>
    public void Clear(ClearBufferMask mask)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        this.Gl.Clear(mask);
    }

    /// <summary>
    /// Applies a blend state configuration to the pipeline.
    /// </summary>
    /// <param name="state">The blend state to apply.</param>
    public void SetBlendState(in BlendState state)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        this.StateManager.SetBlendState(state);
    }

    /// <summary>
    /// Applies a depth stencil state configuration for 2D/2.5D depth testing.
    /// </summary>
    /// <param name="state">The depth stencil state to apply.</param>
    public void SetDepthStencilState(in DepthStencilState state)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        this.StateManager.SetDepthStencilState(state);
    }

    /// <summary>
    /// Configures the scissor test rectangle.
    /// </summary>
    /// <param name="enabled">Whether scissor testing is enabled.</param>
    /// <param name="x">The left coordinate.</param>
    /// <param name="y">The bottom coordinate.</param>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    public void SetScissor(bool enabled, int x = 0, int y = 0, uint width = 0, uint height = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        this.StateManager.SetScissor(enabled, x, y, width, height);
    }

    /// <inheritdoc/>
    public void SetShader(IShader shader)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(shader, nameof(shader));
        shader.Bind();
    }

    /// <inheritdoc/>
    public void SetTexture(int textureUnit, ITexture texture)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(texture, nameof(texture));
        texture.Bind(textureUnit);
    }

    /// <inheritdoc/>
    public void SetRenderTarget(IRenderTarget? renderTarget)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (renderTarget is null)
        {
            this.StateManager.BindFramebuffer(0);
        }
        else
        {
            renderTarget.Bind();
        }
    }

    /// <inheritdoc/>
    public unsafe void DrawElements(PrimitiveType primitiveType, uint indexCount, DrawElementsType indexType, nuint indexOffset = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        this.Gl.DrawElements(primitiveType, indexCount, indexType, (void*)indexOffset);
    }

    /// <inheritdoc/>
    public void DrawArrays(PrimitiveType primitiveType, int first, uint vertexCount)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        this.Gl.DrawArrays(primitiveType, first, vertexCount);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            this.StateManager.Invalidate();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
