// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using System.Runtime.InteropServices;
using Carbon.Graphics.OpenGL.Buffers;
using Silk.NET.OpenGL;

namespace Carbon.Graphics.Batching.Vertices;

/// <summary>
/// Represents an unmanaged vertex for non-textured colored geometry and procedural shapes.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct VertexColor
{
    /// <summary>
    /// The 3D position vector where Z represents the depth coordinate.
    /// </summary>
    public readonly Vector3 Position;

    /// <summary>
    /// The 32-bit packed color (RGBA in little-endian order).
    /// </summary>
    public readonly uint Color;

    /// <summary>
    /// Initializes a new instance of the <see cref="VertexColor"/> struct.
    /// </summary>
    /// <param name="position">The vertex position with depth.</param>
    /// <param name="color">The packed 32-bit color.</param>
    public VertexColor(in Vector3 position, uint color)
    {
        Position = position;
        Color = color;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="VertexColor"/> struct.
    /// </summary>
    /// <param name="x">The X position.</param>
    /// <param name="y">The Y position.</param>
    /// <param name="depth">The depth / Z position for 2.5D ordering.</param>
    /// <param name="color">The packed 32-bit color.</param>
    public VertexColor(float x, float y, float depth, uint color)
    {
        Position = new Vector3(x, y, depth);
        Color = color;
    }

    /// <summary>
    /// Gets the standard vertex layout definition for <see cref="VertexColor"/>.
    /// </summary>
    public static VertexLayout Layout { get; } = new(
        (uint)sizeof(VertexColor),
        [
            new VertexElement(0, 3, VertexAttribPointerType.Float, false, 0),
            new VertexElement(1, 4, VertexAttribPointerType.UnsignedByte, true, 12)
        ]);
}
