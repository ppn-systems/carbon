// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Silk.NET.OpenGL;

namespace Carbon.Graphics.OpenGL.Abstractions;

/// <summary>
/// Defines the contract for an OpenGL index buffer (EBO).
/// </summary>
public interface IIndexBuffer : IDisposable
{
    /// <summary>
    /// Gets the OpenGL buffer handle.
    /// </summary>
    uint Handle { get; }

    /// <summary>
    /// Gets the number of indices in this buffer.
    /// </summary>
    uint IndexCount { get; }

    /// <summary>
    /// Gets the OpenGL element type (UnsignedShort or UnsignedInt).
    /// </summary>
    DrawElementsType IndexType { get; }

    /// <summary>
    /// Binds this buffer to the GL_ELEMENT_ARRAY_BUFFER target.
    /// </summary>
    void Bind();
}
