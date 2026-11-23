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
    /// Sets an integer uniform value.
    /// </summary>
    /// <param name="name">The uniform name declared in shader source.</param>
    /// <param name="value">The integer value.</param>
    void SetUniform(string name, int value);

    /// <summary>
    /// Sets a single-precision floating-point uniform value.
    /// </summary>
    /// <param name="name">The uniform name declared in shader source.</param>
    /// <param name="value">The float value.</param>
    void SetUniform(string name, float value);

    /// <summary>
    /// Sets a 2D vector uniform value.
    /// </summary>
    /// <param name="name">The uniform name declared in shader source.</param>
    /// <param name="value">The 2D vector.</param>
    void SetUniform(string name, in Vector2 value);

    /// <summary>
    /// Sets a 3D vector uniform value.
    /// </summary>
    /// <param name="name">The uniform name declared in shader source.</param>
    /// <param name="value">The 3D vector.</param>
    void SetUniform(string name, in Vector3 value);

    /// <summary>
    /// Sets a 4D vector uniform value.
    /// </summary>
    /// <param name="name">The uniform name declared in shader source.</param>
    /// <param name="value">The 4D vector.</param>
    void SetUniform(string name, in Vector4 value);

    /// <summary>
    /// Sets a 4x4 matrix uniform value (e.g. View-Projection matrix for 2.5D rendering).
    /// </summary>
    /// <param name="name">The uniform name declared in shader source.</param>
    /// <param name="value">The 4x4 matrix.</param>
    void SetUniform(string name, in Matrix4x4 value);
}
