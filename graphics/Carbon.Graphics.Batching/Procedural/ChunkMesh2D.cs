// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Vertices;
using Carbon.Graphics.OpenGL.Abstractions;
using Carbon.Graphics.OpenGL.Buffers;
using Carbon.Graphics.OpenGL.Device;
using Carbon.Graphics.OpenGL.States;
using Silk.NET.OpenGL;
using VertexArray = Carbon.Graphics.OpenGL.Buffers.VertexArray;

namespace Carbon.Graphics.Batching.Procedural;

/// <summary>
/// Represents a pre-baked static GPU mesh for a procedural map chunk, rendering hundreds of tiles in a single draw call.
/// </summary>
public sealed class ChunkMesh2D : IDisposable
{
    private readonly IGraphicsDevice _device;
    private readonly VertexBuffer<Vertex2D> _vbo;
    private readonly IndexBuffer _ebo;
    private readonly VertexArray _vao;
    private bool _disposed;

    /// <summary>
    /// Gets the shared texture atlas used by this chunk mesh.
    /// </summary>
    public ITexture TextureAtlas { get; }

    /// <summary>
    /// Gets the axis-aligned world bounding box for camera frustum culling.
    /// </summary>
    public RectangleF WorldBounds { get; }

    /// <summary>
    /// Gets the number of indices in this chunk mesh.
    /// </summary>
    public uint IndexCount => _ebo.IndexCount;

    /// <summary>
    /// Gets a value indicating whether this chunk contains no geometry.
    /// </summary>
    public bool IsEmpty => this.IndexCount == 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChunkMesh2D"/> class by uploading geometry to static GPU buffers.
    /// </summary>
    /// <param name="device">The graphics device.</param>
    /// <param name="textureAtlas">The texture atlas providing tile sprites.</param>
    /// <param name="worldBounds">The spatial bounds of the chunk.</param>
    /// <param name="vertices">The static vertex span.</param>
    /// <param name="indices">The static index span.</param>
    public ChunkMesh2D(
        IGraphicsDevice device,
        ITexture textureAtlas,
        in RectangleF worldBounds,
        ReadOnlySpan<Vertex2D> vertices,
        ReadOnlySpan<uint> indices)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        this.TextureAtlas = textureAtlas ?? throw new ArgumentNullException(nameof(textureAtlas));
        this.WorldBounds = worldBounds;

        GlStateCache stateCache = device is GraphicsDevice concrete
            ? concrete.StateCache
            : throw new ArgumentException("Device must be a valid GraphicsDevice instance.", nameof(device));

        // Unbind any active VAO so new buffer allocations cannot corrupt previously bound VAO state
        stateCache.BindVertexArray(0);

        uint vertexCapacity = (uint)Math.Max(1, vertices.Length);
        uint indexCapacity = (uint)Math.Max(1, indices.Length);

        _vbo = new VertexBuffer<Vertex2D>(device.Gl, stateCache, vertexCapacity, BufferUsage.StaticDraw);
        _ebo = new IndexBuffer(device.Gl, stateCache, indexCapacity, DrawElementsType.UnsignedInt, BufferUsage.StaticDraw);
        _vao = new VertexArray(device.Gl, stateCache);

        _vao.AttachVertexBuffer(_vbo, Vertex2D.Layout);
        _vao.AttachIndexBuffer(_ebo);

        if (!vertices.IsEmpty)
        {
            _vbo.SetData(vertices);
        }

        if (!indices.IsEmpty)
        {
            _ebo.SetData(indices);
        }

        // Reset active VAO binding to cleanly avoid cross-contamination
        stateCache.BindVertexArray(0);
    }

    /// <summary>
    /// Draws the entire procedural chunk mesh in a single high-efficiency GPU draw call.
    /// </summary>
    public void Draw()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (this.IsEmpty)
        {
            return;
        }

        this.TextureAtlas.Bind(0);
        _vao.Bind();

        _device.DrawElements(PrimitiveType.Triangles, this.IndexCount, DrawElementsType.UnsignedInt);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _vbo.Dispose();
            _ebo.Dispose();
            _vao.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
