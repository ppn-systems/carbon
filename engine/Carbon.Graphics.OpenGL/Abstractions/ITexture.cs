// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Graphics.OpenGL.Abstractions;

/// <summary>
/// Defines the contract for a GPU 2D texture resource.
/// </summary>
public interface ITexture : IDisposable
{
    /// <summary>
    /// Gets the OpenGL texture handle.
    /// </summary>
    uint Handle { get; }

    /// <summary>
    /// Gets the width of the texture in pixels.
    /// </summary>
    uint Width { get; }

    /// <summary>
    /// Gets the height of the texture in pixels.
    /// </summary>
    uint Height { get; }

    /// <summary>
    /// Binds the texture to the specified OpenGL texture unit.
    /// </summary>
    /// <param name="unit">The zero-based texture unit index (0 for GL_TEXTURE0).</param>
    void Bind(int unit = 0);
}
