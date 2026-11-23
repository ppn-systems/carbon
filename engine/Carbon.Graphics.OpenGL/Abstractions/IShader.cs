// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;

namespace Carbon.Graphics.OpenGL.Abstractions;

/// <summary>
/// Defines the contract for an OpenGL shader program and its uniform bindings.
/// </summary>
public interface IShader : IDisposable
{
    /// <summary>
    /// Gets the OpenGL program handle.
    /// </summary>
    uint Handle { get; }

    /// <summary>
    /// Binds this shader program to the GPU pipeline.
    /// </summary>
    void Bind();

    /// <summary>
    /// Queries the cached integer uniform location index for high-speed repeated access.
    /// </summary>
    /// <param name="name">The uniform name declared in GLSL source.</param>
    /// <returns>The uniform location index, or -1 if the uniform does not exist.</returns>
    int GetUniformLocation(string name);

    /// <summary>
    /// Sets an integer uniform value by name.
    /// </summary>
    /// <param name="name">The uniform name declared in shader source.</param>
    /// <param name="value">The integer value.</param>
    void SetUniform(string name, int value);

    /// <summary>
    /// Sets an integer uniform value directly by cached location index (zero string hashing).
    /// </summary>
    /// <param name="location">The cached uniform location index.</param>
    /// <param name="value">The integer value.</param>
    void SetUniform(int location, int value);

    /// <summary>
    /// Sets a single-precision floating-point uniform value by name.
    /// </summary>
    /// <param name="name">The uniform name declared in shader source.</param>
    /// <param name="value">The float value.</param>
    void SetUniform(string name, float value);

    /// <summary>
    /// Sets a single-precision floating-point uniform value directly by cached location index.
    /// </summary>
    /// <param name="location">The cached uniform location index.</param>
    /// <param name="value">The float value.</param>
    void SetUniform(int location, float value);

    /// <summary>
    /// Sets a 2D vector uniform value by name.
    /// </summary>
    /// <param name="name">The uniform name declared in shader source.</param>
    /// <param name="value">The 2D vector.</param>
    void SetUniform(string name, in Vector2 value);

    /// <summary>
    /// Sets a 2D vector uniform value directly by cached location index.
    /// </summary>
    /// <param name="location">The cached uniform location index.</param>
    /// <param name="value">The 2D vector.</param>
    void SetUniform(int location, in Vector2 value);

    /// <summary>
    /// Sets a 3D vector uniform value by name.
    /// </summary>
    /// <param name="name">The uniform name declared in shader source.</param>
    /// <param name="value">The 3D vector.</param>
    void SetUniform(string name, in Vector3 value);

    /// <summary>
    /// Sets a 3D vector uniform value directly by cached location index.
    /// </summary>
    /// <param name="location">The cached uniform location index.</param>
    /// <param name="value">The 3D vector.</param>
    void SetUniform(int location, in Vector3 value);

    /// <summary>
    /// Sets a 4D vector uniform value by name.
    /// </summary>
    /// <param name="name">The uniform name declared in shader source.</param>
    /// <param name="value">The 4D vector.</param>
    void SetUniform(string name, in Vector4 value);

    /// <summary>
    /// Sets a 4D vector uniform value directly by cached location index.
    /// </summary>
    /// <param name="location">The cached uniform location index.</param>
    /// <param name="value">The 4D vector.</param>
    void SetUniform(int location, in Vector4 value);

    /// <summary>
    /// Sets a 4x4 matrix uniform value by name (e.g. View-Projection matrix for 2.5D rendering).
    /// </summary>
    /// <param name="name">The uniform name declared in shader source.</param>
    /// <param name="value">The 4x4 matrix.</param>
    void SetUniform(string name, in Matrix4x4 value);

    /// <summary>
    /// Sets a 4x4 matrix uniform value directly by cached location index (zero string hashing).
    /// </summary>
    /// <param name="location">The cached uniform location index.</param>
    /// <param name="value">The 4x4 matrix.</param>
    void SetUniform(int location, in Matrix4x4 value);
}
