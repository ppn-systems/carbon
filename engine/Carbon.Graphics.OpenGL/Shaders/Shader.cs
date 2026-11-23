// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.OpenGL.Abstractions;
using Carbon.Graphics.OpenGL.States;
using Silk.NET.OpenGL;

namespace Carbon.Graphics.OpenGL.Shaders;

/// <summary>
/// Encapsulates an OpenGL shader program composed of vertex and fragment stages with high-speed uniform caching.
/// </summary>
public sealed class Shader : IShader
{
    private readonly GL _gl;
    private readonly GlStateCache _stateCache;
    private readonly ShaderUniformCache _uniformCache;
    private bool _disposed;

    /// <inheritdoc/>
    public uint Handle { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Shader"/> class, compiling and linking vertex and fragment sources.
    /// </summary>
    /// <param name="gl">The OpenGL API instance.</param>
    /// <param name="stateCache">The state cache manager.</param>
    /// <param name="vertexSource">The GLSL source code for the vertex shader.</param>
    /// <param name="fragmentSource">The GLSL source code for the fragment shader.</param>
    public Shader(GL gl, GlStateCache stateCache, string vertexSource, string fragmentSource)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        _stateCache = stateCache ?? throw new ArgumentNullException(nameof(stateCache));
        ArgumentException.ThrowIfNullOrWhiteSpace(vertexSource, nameof(vertexSource));
        ArgumentException.ThrowIfNullOrWhiteSpace(fragmentSource, nameof(fragmentSource));

        uint vertexShader = this.CompileShaderStage(ShaderType.VertexShader, vertexSource);
        uint fragmentShader = this.CompileShaderStage(ShaderType.FragmentShader, fragmentSource);

        this.Handle = _gl.CreateProgram();
        if (this.Handle == 0)
        {
            _gl.DeleteShader(vertexShader);
            _gl.DeleteShader(fragmentShader);
            throw new InvalidOperationException("Failed to create OpenGL shader program.");
        }

        _gl.AttachShader(this.Handle, vertexShader);
        _gl.AttachShader(this.Handle, fragmentShader);
        _gl.LinkProgram(this.Handle);

        _gl.GetProgram(this.Handle, ProgramPropertyARB.LinkStatus, out int linkStatus);
        if (linkStatus == 0)
        {
            string infoLog = _gl.GetProgramInfoLog(this.Handle);
            _gl.DeleteShader(vertexShader);
            _gl.DeleteShader(fragmentShader);
            _gl.DeleteProgram(this.Handle);
            throw new InvalidOperationException($"Shader program linking failed: {infoLog}");
        }

        _gl.DetachShader(this.Handle, vertexShader);
        _gl.DetachShader(this.Handle, fragmentShader);
        _gl.DeleteShader(vertexShader);
        _gl.DeleteShader(fragmentShader);

        _uniformCache = new ShaderUniformCache(_gl, this.Handle);
    }

    /// <inheritdoc/>
    public void Bind()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _stateCache.UseProgram(this.Handle);
    }

    /// <inheritdoc/>
    public int GetUniformLocation(string name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _uniformCache.GetLocation(name);
    }

    /// <inheritdoc/>
    public void SetUniform(string name, int value)
    {
        int location = this.GetUniformLocation(name);
        this.SetUniform(location, value);
    }

    /// <inheritdoc/>
    public void SetUniform(int location, int value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (location != -1)
        {
            this.Bind();
            _gl.Uniform1(location, value);
        }
    }

    /// <inheritdoc/>
    public void SetUniform(string name, float value)
    {
        int location = this.GetUniformLocation(name);
        this.SetUniform(location, value);
    }

    /// <inheritdoc/>
    public void SetUniform(int location, float value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (location != -1)
        {
            this.Bind();
            _gl.Uniform1(location, value);
        }
    }

    /// <inheritdoc/>
    public void SetUniform(string name, in Vector2 value)
    {
        int location = this.GetUniformLocation(name);
        this.SetUniform(location, in value);
    }

    /// <inheritdoc/>
    public void SetUniform(int location, in Vector2 value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (location != -1)
        {
            this.Bind();
            _gl.Uniform2(location, value.X, value.Y);
        }
    }

    /// <inheritdoc/>
    public void SetUniform(string name, in Vector3 value)
    {
        int location = this.GetUniformLocation(name);
        this.SetUniform(location, in value);
    }

    /// <inheritdoc/>
    public void SetUniform(int location, in Vector3 value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (location != -1)
        {
            this.Bind();
            _gl.Uniform3(location, value.X, value.Y, value.Z);
        }
    }

    /// <inheritdoc/>
    public void SetUniform(string name, in Vector4 value)
    {
        int location = this.GetUniformLocation(name);
        this.SetUniform(location, in value);
    }

    /// <inheritdoc/>
    public void SetUniform(int location, in Vector4 value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (location != -1)
        {
            this.Bind();
            _gl.Uniform4(location, value.X, value.Y, value.Z, value.W);
        }
    }

    /// <inheritdoc/>
    public void SetUniform(string name, in Matrix4x4 value)
    {
        int location = this.GetUniformLocation(name);
        this.SetUniform(location, in value);
    }

    /// <inheritdoc/>
    public unsafe void SetUniform(int location, in Matrix4x4 value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (location != -1)
        {
            this.Bind();
            fixed (Matrix4x4* ptr = &value)
            {
                _gl.UniformMatrix4(location, 1, false, (float*)ptr);
            }
        }
    }

    private uint CompileShaderStage(ShaderType type, string source)
    {
        uint shader = _gl.CreateShader(type);
        if (shader == 0)
        {
            throw new InvalidOperationException($"Failed to allocate OpenGL shader of type {type}.");
        }

        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);

        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
        if (status == 0)
        {
            string log = _gl.GetShaderInfoLog(shader);
            _gl.DeleteShader(shader);
            throw new InvalidOperationException($"Compilation error in {type}: {log}");
        }

        return shader;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _gl.DeleteProgram(this.Handle);
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
