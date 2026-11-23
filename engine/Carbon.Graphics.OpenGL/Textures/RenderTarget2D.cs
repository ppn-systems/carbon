using System;
using Carbon.Graphics.OpenGL.Abstractions;
using Carbon.Graphics.OpenGL.States;
using Silk.NET.OpenGL;

namespace Carbon.Graphics.OpenGL.Textures;

/// <summary>
/// Encapsulates an off-screen Framebuffer Object (FBO) backed by a 2D color texture and an optional depth-stencil buffer.
/// </summary>
public sealed class RenderTarget2D : IRenderTarget
{
    private readonly GL _gl;
    private readonly GlStateManager _stateManager;
    private readonly uint _depthRenderbufferHandle;
    private bool _disposed;

    /// <inheritdoc/>
    public uint FramebufferHandle { get; }

    /// <inheritdoc/>
    ITexture IRenderTarget.ColorTexture => this.ColorTexture;

    /// <summary>
    /// Gets the concrete <see cref="Texture2D"/> receiving color rendering.
    /// </summary>
    public Texture2D ColorTexture { get; }

    /// <inheritdoc/>
    public uint Width => this.ColorTexture.Width;

    /// <inheritdoc/>
    public uint Height => this.ColorTexture.Height;

    /// <summary>
    /// Gets a value indicating whether this render target includes a depth/stencil buffer for 2.5D rendering.
    /// </summary>
    public bool HasDepthStencil => _depthRenderbufferHandle != 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="RenderTarget2D"/> class.
    /// </summary>
    /// <param name="gl">The OpenGL API instance.</param>
    /// <param name="stateManager">The state cache manager.</param>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="withDepthStencil">Whether to attach a 24-bit depth and 8-bit stencil renderbuffer.</param>
    public RenderTarget2D(GL gl, GlStateManager stateManager, uint width, uint height, bool withDepthStencil = true)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));

        this.ColorTexture = new Texture2D(gl, stateManager, width, height, PixelFormatType.Rgba8, TextureFilter.Nearest, TextureWrap.ClampToEdge);

        this.FramebufferHandle = _gl.GenFramebuffer();
        _stateManager.BindFramebuffer(this.FramebufferHandle);

        _gl.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D,
            this.ColorTexture.Handle,
            0);

        if (withDepthStencil)
        {
            _depthRenderbufferHandle = _gl.GenRenderbuffer();
            _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depthRenderbufferHandle);
            _gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.Depth24Stencil8, width, height);
            _gl.FramebufferRenderbuffer(
                FramebufferTarget.Framebuffer,
                FramebufferAttachment.DepthStencilAttachment,
                RenderbufferTarget.Renderbuffer,
                _depthRenderbufferHandle);
        }

        GLEnum status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
        {
            this.Dispose();
            throw new InvalidOperationException($"OpenGL Framebuffer configuration failed with status: {status}");
        }

        _stateManager.BindFramebuffer(0);
    }

    /// <inheritdoc/>
    public void Bind()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _stateManager.BindFramebuffer(this.FramebufferHandle);
        _stateManager.SetViewport(0, 0, this.Width, this.Height);
    }

    /// <inheritdoc/>
    public void Unbind()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _stateManager.BindFramebuffer(0);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            if (_depthRenderbufferHandle != 0)
            {
                _gl.DeleteRenderbuffer(_depthRenderbufferHandle);
            }

            if (this.FramebufferHandle != 0)
            {
                _gl.DeleteFramebuffer(this.FramebufferHandle);
            }

            this.ColorTexture.Dispose();
            _disposed = true;
        }
    }
}
