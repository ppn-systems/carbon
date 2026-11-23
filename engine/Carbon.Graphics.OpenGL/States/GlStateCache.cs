// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Silk.NET.OpenGL;

namespace Carbon.Graphics.OpenGL.States;

/// <summary>
/// High-performance OpenGL pipeline state cache that eliminates redundant driver calls.
/// </summary>
public sealed class GlStateCache
{
    private const int MAX_TEXTURE_UNITS = 32;

    private readonly GL _gl;
    private readonly uint[] _boundTextures = new uint[MAX_TEXTURE_UNITS];

    private uint _currentProgram;
    private uint _currentVao;
    private uint _currentVbo;
    private uint _currentEbo;
    private uint _currentFbo;
    private int _activeTextureUnit = -1;

    private BlendState _currentBlendState;
    private bool _blendEnabled;

    private DepthStencilState _currentDepthState;
    private bool _depthTestEnabled;

    private int _viewportX = -1;
    private int _viewportY = -1;
    private uint _viewportWidth;
    private uint _viewportHeight;

    private bool _scissorEnabled;
    private int _scissorX = -1;
    private int _scissorY = -1;
    private uint _scissorWidth;
    private uint _scissorHeight;

    /// <summary>
    /// Gets the currently cached viewport rectangle (X, Y, Width, Height) without querying the GPU.
    /// </summary>
    public (int X, int Y, uint Width, uint Height) Viewport => (_viewportX, _viewportY, _viewportWidth, _viewportHeight);

    /// <summary>
    /// Gets the currently cached scissor rectangle and its enabled status without querying the GPU.
    /// </summary>
    public (int X, int Y, uint Width, uint Height, bool Enabled) Scissor => (_scissorX, _scissorY, _scissorWidth, _scissorHeight, _scissorEnabled);

    /// <summary>
    /// Gets the currently cached color blend state.
    /// </summary>
    public BlendState CurrentBlendState => _currentBlendState;

    /// <summary>
    /// Gets the currently cached depth stencil state.
    /// </summary>
    public DepthStencilState CurrentDepthState => _currentDepthState;

    /// <summary>
    /// Initializes a new instance of the <see cref="GlStateCache"/> class.
    /// </summary>
    /// <param name="gl">The Silk.NET OpenGL API instance.</param>
    public GlStateCache(GL gl) => _gl = gl ?? throw new ArgumentNullException(nameof(gl));

    /// <summary>
    /// Sets the active shader program, avoiding redundant glUseProgram calls.
    /// </summary>
    /// <param name="program">The shader program handle.</param>
    public void UseProgram(uint program)
    {
        if (_currentProgram != program)
        {
            _currentProgram = program;
            _gl.UseProgram(program);
        }
    }

    /// <summary>
    /// Sets the active vertex array object, avoiding redundant glBindVertexArray calls.
    /// </summary>
    /// <param name="vao">The VAO handle.</param>
    public void BindVertexArray(uint vao)
    {
        if (_currentVao != vao)
        {
            _currentVao = vao;
            _gl.BindVertexArray(vao);
        }
    }

    /// <summary>
    /// Sets the active vertex buffer, avoiding redundant glBindBuffer calls.
    /// </summary>
    /// <param name="vbo">The VBO handle.</param>
    public void BindVertexBuffer(uint vbo)
    {
        if (_currentVbo != vbo)
        {
            _currentVbo = vbo;
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        }
    }

    /// <summary>
    /// Sets the active index buffer, avoiding redundant glBindBuffer calls.
    /// </summary>
    /// <param name="ebo">The EBO handle.</param>
    public void BindIndexBuffer(uint ebo)
    {
        if (_currentEbo != ebo)
        {
            _currentEbo = ebo;
            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, ebo);
        }
    }

    /// <summary>
    /// Sets the active framebuffer object, avoiding redundant glBindFramebuffer calls.
    /// </summary>
    /// <param name="fbo">The FBO handle.</param>
    public void BindFramebuffer(uint fbo)
    {
        if (_currentFbo != fbo)
        {
            _currentFbo = fbo;
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
        }
    }

    /// <summary>
    /// Binds a 2D texture to the specified texture unit, caching active unit and texture handle.
    /// </summary>
    /// <param name="unit">The zero-based texture unit (0 for GL_TEXTURE0).</param>
    /// <param name="texture">The OpenGL texture handle.</param>
    public void BindTexture(int unit, uint texture)
    {
        if ((uint)unit >= MAX_TEXTURE_UNITS)
        {
            throw new ArgumentOutOfRangeException(nameof(unit), unit, "Texture unit exceeds supported capacity.");
        }

        if (_activeTextureUnit != unit)
        {
            _activeTextureUnit = unit;
            _gl.ActiveTexture((TextureUnit)((int)TextureUnit.Texture0 + unit));
        }

        if (_boundTextures[unit] != texture)
        {
            _boundTextures[unit] = texture;
            _gl.BindTexture(TextureTarget.Texture2D, texture);
        }
    }

    /// <summary>
    /// Applies color blending states, eliminating duplicate blend configuration calls.
    /// </summary>
    /// <param name="state">The desired blend state.</param>
    public void SetBlendState(in BlendState state)
    {
        if (!_blendEnabled)
        {
            _gl.Enable(EnableCap.Blend);
            _blendEnabled = true;
        }

        if (_currentBlendState != state)
        {
            _currentBlendState = state;
            _gl.BlendFuncSeparate(state.ColorSource, state.ColorDestination, state.AlphaSource, state.AlphaDestination);
            _gl.BlendEquationSeparate(state.ColorEquation, state.AlphaEquation);
        }
    }

    /// <summary>
    /// Disables color blending if currently enabled.
    /// </summary>
    public void DisableBlend()
    {
        if (_blendEnabled)
        {
            _gl.Disable(EnableCap.Blend);
            _blendEnabled = false;
        }
    }

    /// <summary>
    /// Applies depth stencil configuration, eliminating duplicate depth calls.
    /// </summary>
    /// <param name="state">The desired depth stencil state.</param>
    public void SetDepthStencilState(in DepthStencilState state)
    {
        if (state.DepthTestEnabled)
        {
            if (!_depthTestEnabled)
            {
                _gl.Enable(EnableCap.DepthTest);
                _depthTestEnabled = true;
            }

            if (_currentDepthState.DepthFunction != state.DepthFunction)
            {
                _gl.DepthFunc(state.DepthFunction);
            }

            if (_currentDepthState.DepthWriteEnabled != state.DepthWriteEnabled)
            {
                _gl.DepthMask(state.DepthWriteEnabled);
            }
        }
        else
        {
            if (_depthTestEnabled)
            {
                _gl.Disable(EnableCap.DepthTest);
                _depthTestEnabled = false;
            }

            if (_currentDepthState.DepthWriteEnabled != state.DepthWriteEnabled)
            {
                _gl.DepthMask(state.DepthWriteEnabled);
            }
        }

        _currentDepthState = state;
    }

    /// <summary>
    /// Sets the viewport rectangle, avoiding duplicate glViewport calls.
    /// </summary>
    /// <param name="x">The left coordinate.</param>
    /// <param name="y">The bottom coordinate.</param>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    public void SetViewport(int x, int y, uint width, uint height)
    {
        if (_viewportX != x || _viewportY != y || _viewportWidth != width || _viewportHeight != height)
        {
            _viewportX = x;
            _viewportY = y;
            _viewportWidth = width;
            _viewportHeight = height;
            _gl.Viewport(x, y, width, height);
        }
    }

    /// <summary>
    /// Configures the scissor test rectangle, avoiding duplicate glScissor calls.
    /// </summary>
    /// <param name="enabled">Whether scissor testing is enabled.</param>
    /// <param name="x">The left coordinate.</param>
    /// <param name="y">The bottom coordinate.</param>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    public void SetScissor(bool enabled, int x = 0, int y = 0, uint width = 0, uint height = 0)
    {
        if (enabled)
        {
            if (!_scissorEnabled)
            {
                _gl.Enable(EnableCap.ScissorTest);
                _scissorEnabled = true;
            }

            if (_scissorX != x || _scissorY != y || _scissorWidth != width || _scissorHeight != height)
            {
                _scissorX = x;
                _scissorY = y;
                _scissorWidth = width;
                _scissorHeight = height;
                _gl.Scissor(x, y, width, height);
            }
        }
        else if (_scissorEnabled)
        {
            _gl.Disable(EnableCap.ScissorTest);
            _scissorEnabled = false;
        }
    }

    /// <summary>
    /// Invalidates all cached state bindings (useful when switching external render contexts).
    /// </summary>
    public void Invalidate()
    {
        _currentProgram = 0;
        _currentVao = 0;
        _currentVbo = 0;
        _currentEbo = 0;
        _currentFbo = 0;
        _activeTextureUnit = -1;
        Array.Clear(_boundTextures, 0, _boundTextures.Length);

        _blendEnabled = false;
        _depthTestEnabled = false;
        _viewportX = -1;
        _scissorX = -1;
        _scissorEnabled = false;
    }
}
