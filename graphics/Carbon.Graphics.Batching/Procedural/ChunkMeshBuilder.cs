// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Vertices;
using Carbon.Graphics.OpenGL.Abstractions;

namespace Carbon.Graphics.Batching.Procedural;

/// <summary>
/// Assembles procedural terrain cells, tiles, and walls into a high-performance static <see cref="ChunkMesh2D"/>.
/// </summary>
public sealed class ChunkMeshBuilder
{
    private readonly List<Vertex2D> _vertices = new(1024);
    private readonly List<uint> _indices = new(1536);

    /// <summary>
    /// Gets the number of vertices currently recorded in this builder.
    /// </summary>
    public int VertexCount => _vertices.Count;

    /// <summary>
    /// Adds a 2D/2.5D rectangular tile to the chunk mesh.
    /// </summary>
    /// <param name="position">Top-left world coordinate of the tile.</param>
    /// <param name="size">Width and height of the tile in world units.</param>
    /// <param name="uvRect">Normalized UV rectangle on the texture atlas (0.0 to 1.0).</param>
    /// <param name="color">Vertex tint color.</param>
    /// <param name="depth">Depth Z coordinate.</param>
    public void AddTile(Vector2 position, Vector2 size, in RectangleF uvRect, ColorRgba color, float depth = 0f)
    {
        uint baseIndex = (uint)_vertices.Count;
        uint packedColor = color.PackedValue;

        float x0 = position.X;
        float y0 = position.Y;
        float x1 = position.X + size.X;
        float y1 = position.Y + size.Y;

        float u0 = uvRect.Left;
        float v0 = uvRect.Top;
        float u1 = uvRect.Right;
        float v1 = uvRect.Bottom;

        _vertices.Add(new Vertex2D(x0, y0, depth, packedColor, u0, v0));
        _vertices.Add(new Vertex2D(x1, y0, depth, packedColor, u1, v0));
        _vertices.Add(new Vertex2D(x1, y1, depth, packedColor, u1, v1));
        _vertices.Add(new Vertex2D(x0, y1, depth, packedColor, u0, v1));

        _indices.Add(baseIndex + 0);
        _indices.Add(baseIndex + 1);
        _indices.Add(baseIndex + 2);
        _indices.Add(baseIndex + 2);
        _indices.Add(baseIndex + 3);
        _indices.Add(baseIndex + 0);
    }

    /// <summary>
    /// Compiles the accumulated tiles and geometry into an immutable GPU-backed <see cref="ChunkMesh2D"/>.
    /// </summary>
    /// <param name="device">The graphics device.</param>
    /// <param name="textureAtlas">The shared texture atlas.</param>
    /// <param name="worldBounds">The spatial boundary rectangle of the chunk.</param>
    /// <returns>A new <see cref="ChunkMesh2D"/> instance.</returns>
    public ChunkMesh2D Build(IGraphicsDevice device, ITexture textureAtlas, in RectangleF worldBounds)
    {
        ChunkMesh2D chunk = new(
            device,
            textureAtlas,
            worldBounds,
            CollectionsMarshalHelper.AsSpan(_vertices),
            CollectionsMarshalHelper.AsSpan(_indices));

        this.Clear();
        return chunk;
    }

    /// <summary>
    /// Clears all accumulated vertices and indices to prepare for building the next chunk mesh.
    /// </summary>
    public void Clear()
    {
        _vertices.Clear();
        _indices.Clear();
    }

    private static class CollectionsMarshalHelper
    {
        public static ReadOnlySpan<T> AsSpan<T>(List<T> list) =>
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
    }
}
