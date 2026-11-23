// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Graphics.OpenGL.Abstractions;
using Carbon.Graphics.OpenGL.States;
using Silk.NET.OpenGL;

namespace Carbon.Graphics.OpenGL.Buffers;

/// <summary>
/// Represents a strongly-typed OpenGL vertex buffer object (VBO) storing unmanaged vertex structures.
/// </summary>
/// <typeparam name="TVertex">The unmanaged vertex struct type.</typeparam>
public sealed class VertexBuffer<TVertex> : IVertexBuffer where TVertex : unmanaged
{
    private readonly GL _gl;
    private readonly GlStateCache _stateCache;
    private bool _disposed;

    /// <inheritdoc/>
    public uint Handle { get; }

    /// <inheritdoc/>
    public uint Stride { get; }

    /// <inheritdoc/>
    public uint VertexCount { get; private set; }

    /// <summary>
    /// Gets the usage pattern configured for this buffer.
    /// </summary>
    public BufferUsage Usage { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="VertexBuffer{TVertex}"/> class with pre-allocated capacity.
    /// </summary>
    /// <param name="gl">The OpenGL API instance.</param>
    /// <param name="stateCache">The state cache manager.</param>
    /// <param name="capacity">The maximum number of vertices this buffer can initially store.</param>
    /// <param name="usage">The expected buffer usage hint.</param>
    public unsafe VertexBuffer(GL gl, GlStateCache stateCache, uint capacity, BufferUsage usage = BufferUsage.DynamicDraw)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        _stateCache = stateCache ?? throw new ArgumentNullException(nameof(stateCache));
        ArgumentOutOfRangeException.ThrowIfZero(capacity, nameof(capacity));

        this.Stride = (uint)sizeof(TVertex);
        this.VertexCount = capacity;
        this.Usage = usage;

        this.Handle = _gl.GenBuffer();
        _stateCache.BindVertexBuffer(this.Handle);

        BufferUsageARB glUsage = ResolveGlUsage(usage);
        _gl.BufferData(BufferTargetARB.ArrayBuffer, capacity * this.Stride, null, glUsage);
    }

    /// <inheritdoc/>
    public void Bind()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _stateCache.BindVertexBuffer(this.Handle);
    }

    /// <summary>
    /// Uploads vertex data to the GPU using high-performance pointer sub-data streaming.
    /// </summary>
    /// <param name="vertices">The span of vertex data to write.</param>
    /// <param name="offsetVertices">The vertex index offset within the buffer.</param>
    /// <param name="discardBuffer">Whether to orphan (discard) the existing buffer backing to prevent GPU pipeline stalls on dynamic streaming.</param>
    public unsafe void SetData(ReadOnlySpan<TVertex> vertices, uint offsetVertices = 0, bool discardBuffer = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (vertices.IsEmpty)
        {
            return;
        }

        if (offsetVertices + (uint)vertices.Length > this.VertexCount)
        {
            throw new ArgumentOutOfRangeException(nameof(vertices), "Vertex data exceeds buffer capacity.");
        }

        this.Bind();

        if (discardBuffer && offsetVertices == 0)
        {
            BufferUsageARB glUsage = ResolveGlUsage(this.Usage);
            _gl.BufferData(BufferTargetARB.ArrayBuffer, this.VertexCount * this.Stride, null, glUsage);
        }

        fixed (TVertex* ptr = vertices)
        {
            nuint sizeBytes = (nuint)(vertices.Length * sizeof(TVertex));
            nint offsetBytes = (nint)(offsetVertices * sizeof(TVertex));
            _gl.BufferSubData(BufferTargetARB.ArrayBuffer, offsetBytes, sizeBytes, ptr);
        }
    }

    private static BufferUsageARB ResolveGlUsage(BufferUsage usage) => usage switch
    {
        BufferUsage.StaticDraw => BufferUsageARB.StaticDraw,
        BufferUsage.DynamicDraw => BufferUsageARB.DynamicDraw,
        BufferUsage.StreamDraw => BufferUsageARB.StreamDraw,
        _ => BufferUsageARB.DynamicDraw
    };

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _gl.DeleteBuffer(this.Handle);
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
