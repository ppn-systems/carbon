// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Graphics.OpenGL.States;
using Silk.NET.OpenGL;

namespace Carbon.Graphics.OpenGL.Textures;

/// <summary>
/// An offscreen colour target (ADR-041): a framebuffer object with one linear-filtered RGBA8 colour texture and no
/// depth buffer, for the world scene, the light map and post passes. Created once per size; nothing is allocated
/// while it is bound or sampled.
/// </summary>
public sealed class RenderTarget2D : IDisposable
{
    private readonly GL _gl;
    private readonly GlStateCache _stateCache;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RenderTarget2D"/> class.
    /// </summary>
    /// <param name="gl">The OpenGL API instance.</param>
    /// <param name="stateCache">The state cache manager.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <exception cref="InvalidOperationException">The driver reports the framebuffer incomplete.</exception>
    public RenderTarget2D(GL gl, GlStateCache stateCache, uint width, uint height)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        _stateCache = stateCache ?? throw new ArgumentNullException(nameof(stateCache));
        this.Texture = new Texture2D(gl, stateCache, width, height, TextureFormat.Rgba8, TextureFilter.Linear, TextureWrap.ClampToEdge);
        this.Handle = _gl.GenFramebuffer();
        _stateCache.BindFramebuffer(this.Handle);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, this.Texture.Handle, 0);
        GLEnum status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        _stateCache.BindFramebuffer(0);
        if (status != GLEnum.FramebufferComplete)
        {
            this.Dispose();
            throw new InvalidOperationException($"Render target {width}x{height} is incomplete: {status}.");
        }
    }

    /// <summary>Gets the framebuffer object handle.</summary>
    public uint Handle { get; }

    /// <summary>Gets the colour texture the target renders into.</summary>
    public Texture2D Texture { get; }

    /// <summary>Gets the width in pixels.</summary>
    public uint Width => this.Texture.Width;

    /// <summary>Gets the height in pixels.</summary>
    public uint Height => this.Texture.Height;

    /// <summary>Binds the target for drawing and sets the viewport to cover it.</summary>
    public void Bind()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _stateCache.BindFramebuffer(this.Handle);
        _stateCache.SetViewport(0, 0, this.Width, this.Height);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _stateCache.InvalidateFramebuffer(this.Handle);
            _gl.DeleteFramebuffer(this.Handle);
            this.Texture.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
