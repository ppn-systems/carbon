// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Silk.NET.OpenGL;

namespace Carbon.Graphics.OpenGL.Abstractions;

/// <summary>
/// Defines the high-level graphics device interface for low-level GPU operations.
/// </summary>
public interface IGraphicsDevice : IDisposable
{
    /// <summary>
    /// Gets the underlying Silk.NET OpenGL API instance.
    /// </summary>
    GL Gl { get; }

    /// <summary>
    /// Gets a value indicating whether the graphics device and GL context are active and valid.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Sets the active viewport rectangle in screen coordinates.
    /// </summary>
    /// <param name="x">The left coordinate of the viewport.</param>
    /// <param name="y">The bottom coordinate of the viewport.</param>
    /// <param name="width">The width of the viewport in pixels.</param>
    /// <param name="height">The height of the viewport in pixels.</param>
    void SetViewport(int x, int y, uint width, uint height);

    /// <summary>
    /// Sets the clear color for the active framebuffer.
    /// </summary>
    /// <param name="color">The clear color vector (R, G, B, A).</param>
    void SetClearColor(in Vector4 color);

    /// <summary>
    /// Clears the specified buffers of the currently bound framebuffer.
    /// </summary>
    /// <param name="mask">The buffer bitmask specifying color, depth, and/or stencil buffers.</param>
    void Clear(ClearBufferMask mask);

    /// <summary>
    /// Binds a shader program to the GPU pipeline.
    /// </summary>
    /// <param name="shader">The shader to bind.</param>
    void SetShader(IShader shader);

    /// <summary>
    /// Binds a texture to a specific texture unit.
    /// </summary>
    /// <param name="textureUnit">The zero-based texture unit index (0 for GL_TEXTURE0).</param>
    /// <param name="texture">The texture to bind.</param>
    void SetTexture(int textureUnit, ITexture texture);

    /// <summary>
    /// Executes an indexed draw call using the currently bound VAO, VBO, EBO, and shader.
    /// </summary>
    /// <param name="primitiveType">The OpenGL primitive type (e.g. Triangles).</param>
    /// <param name="indexCount">The number of indices to render.</param>
    /// <param name="indexType">The data type of indices (UnsignedShort or UnsignedInt).</param>
    /// <param name="indexOffset">The byte offset into the index buffer.</param>
    void DrawElements(PrimitiveType primitiveType, uint indexCount, DrawElementsType indexType, nuint indexOffset = 0);

    /// <summary>
    /// Executes a non-indexed draw call using the currently bound VAO, VBO, and shader.
    /// </summary>
    /// <param name="primitiveType">The OpenGL primitive type (e.g. Triangles, Lines).</param>
    /// <param name="first">The starting vertex index.</param>
    /// <param name="vertexCount">The number of vertices to render.</param>
    void DrawArrays(PrimitiveType primitiveType, int first, uint vertexCount);
}
