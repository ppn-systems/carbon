// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using System.Runtime.InteropServices;
using Carbon.Graphics.OpenGL.Buffers;
using Silk.NET.OpenGL;

namespace Carbon.Graphics.Batching.Vertices;

/// <summary>
/// Represents an unmanaged 2D/2.5D vertex containing position with depth, packed 32-bit color, and texture coordinates.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct Vertex2D
{
    /// <summary>
    /// The 3D position vector where Z represents the depth coordinate for 2.5D rendering.
    /// </summary>
    public readonly Vector3 Position;

    /// <summary>
    /// The 32-bit packed color (RGBA in little-endian order).
    /// </summary>
    public readonly uint Color;

    /// <summary>
    /// The normalized UV texture coordinates.
    /// </summary>
    public readonly Vector2 TexCoord;

    /// <summary>
    /// Initializes a new instance of the <see cref="Vertex2D"/> struct.
    /// </summary>
    /// <param name="position">The vertex position with depth.</param>
    /// <param name="color">The packed 32-bit color.</param>
    /// <param name="texCoord">The UV texture coordinates.</param>
    public Vertex2D(in Vector3 position, uint color, in Vector2 texCoord)
    {
        Position = position;
        Color = color;
        TexCoord = texCoord;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Vertex2D"/> struct with 2D position and explicit depth.
    /// </summary>
    /// <param name="x">The X position.</param>
    /// <param name="y">The Y position.</param>
    /// <param name="depth">The depth / Z position for 2.5D ordering.</param>
    /// <param name="color">The packed 32-bit color.</param>
    /// <param name="u">The horizontal texture coordinate.</param>
    /// <param name="v">The vertical texture coordinate.</param>
    public Vertex2D(float x, float y, float depth, uint color, float u, float v)
    {
        Position = new Vector3(x, y, depth);
        Color = color;
        TexCoord = new Vector2(u, v);
    }

    /// <summary>
    /// Gets the standard vertex layout definition for <see cref="Vertex2D"/>.
    /// </summary>
    public static VertexLayout Layout { get; } = new(
        (uint)sizeof(Vertex2D),
        [
            new VertexElement(0, 3, VertexAttribPointerType.Float, false, 0),
            new VertexElement(1, 4, VertexAttribPointerType.UnsignedByte, true, 12),
            new VertexElement(2, 2, VertexAttribPointerType.Float, false, 16)
        ]);
}
