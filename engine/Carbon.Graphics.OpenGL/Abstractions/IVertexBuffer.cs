// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Graphics.OpenGL.Abstractions;

/// <summary>
/// Defines the non-generic contract for binding a vertex buffer.
/// </summary>
public interface IVertexBuffer : IDisposable
{
    /// <summary>
    /// Gets the OpenGL buffer handle.
    /// </summary>
    uint Handle { get; }

    /// <summary>
    /// Gets the byte stride of each vertex in the buffer.
    /// </summary>
    uint Stride { get; }

    /// <summary>
    /// Gets the current capacity in number of vertices.
    /// </summary>
    uint VertexCount { get; }

    /// <summary>
    /// Binds this buffer to the GL_ARRAY_BUFFER target.
    /// </summary>
    void Bind();
}
