// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Graphics.OpenGL.Abstractions;
using Carbon.Graphics.OpenGL.States;
using Silk.NET.OpenGL;

namespace Carbon.Graphics.OpenGL.Buffers;

/// <summary>
/// Represents an OpenGL index buffer object (EBO) supporting 16-bit and 32-bit index types.
/// </summary>
public sealed class IndexBuffer : IIndexBuffer
{
    private readonly GL _gl;
    private readonly GlStateCache _stateCache;
    private bool _disposed;
    private nuint _storeBytes;

    /// <inheritdoc/>
    public uint Handle { get; }

    /// <inheritdoc/>
    public uint IndexCount { get; private set; }

    /// <inheritdoc/>
    public DrawElementsType IndexType { get; }

    /// <summary>
    /// Gets the usage pattern configured for this buffer.
    /// </summary>
    public BufferUsage Usage { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="IndexBuffer"/> class with pre-allocated capacity.
    /// </summary>
    /// <param name="gl">The OpenGL API instance.</param>
    /// <param name="stateCache">The state cache manager.</param>
    /// <param name="capacity">The maximum number of indices this buffer can store.</param>
    /// <param name="indexType">The index element type (UnsignedShort or UnsignedInt).</param>
    /// <param name="usage">The expected buffer usage hint.</param>
    public unsafe IndexBuffer(
        GL gl,
        GlStateCache stateCache,
        uint capacity,
        DrawElementsType indexType = DrawElementsType.UnsignedShort,
        BufferUsage usage = BufferUsage.DynamicDraw)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        _stateCache = stateCache ?? throw new ArgumentNullException(nameof(stateCache));
        ArgumentOutOfRangeException.ThrowIfZero(capacity, nameof(capacity));

        this.IndexCount = capacity;
        this.IndexType = indexType;
        this.Usage = usage;

        uint elementSize = indexType switch
        {
            DrawElementsType.UnsignedShort => sizeof(ushort),
            DrawElementsType.UnsignedInt => sizeof(uint),
            DrawElementsType.UnsignedByte or _ => throw new ArgumentException("Only UnsignedShort and UnsignedInt index types are supported.", nameof(indexType))
        };

        this.Handle = _gl.GenBuffer();
        _stateCache.BindVertexArray(0);
        _stateCache.BindIndexBuffer(this.Handle);

        BufferUsageARB glUsage = ResolveGlUsage(usage);
        _gl.BufferData(BufferTargetARB.ElementArrayBuffer, capacity * elementSize, null, glUsage);
        _storeBytes = capacity * elementSize;
    }

    /// <inheritdoc/>
    public void Bind()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _stateCache.BindIndexBuffer(this.Handle);
    }

    /// <summary>
    /// Uploads 16-bit unsigned short indices into the GPU buffer.
    /// </summary>
    /// <param name="indices">The span of 16-bit indices.</param>
    /// <param name="offsetIndices">The index offset within the buffer.</param>
    /// <param name="discardBuffer">Whether to orphan the existing buffer backing to prevent GPU stalls.</param>
    public unsafe void SetData(ReadOnlySpan<ushort> indices, uint offsetIndices = 0, bool discardBuffer = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (this.IndexType != DrawElementsType.UnsignedShort)
        {
            throw new InvalidOperationException("Index buffer was configured for 32-bit unsigned integers.");
        }

        if (indices.IsEmpty)
        {
            return;
        }

        if (offsetIndices + (uint)indices.Length > this.IndexCount)
        {
            throw new ArgumentOutOfRangeException(nameof(indices), "Index data exceeds buffer capacity.");
        }

        this.Bind();

        if (discardBuffer && offsetIndices == 0)
        {
            BufferUsageARB glUsage = ResolveGlUsage(this.Usage);
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer, this.IndexCount * sizeof(ushort), null, glUsage);
            _storeBytes = this.IndexCount * sizeof(ushort);
        }

        fixed (ushort* ptr = indices)
        {
            nuint sizeBytes = (nuint)(indices.Length * sizeof(ushort));
            nint offsetBytes = (nint)(offsetIndices * sizeof(ushort));
            EnsureStoreHolds(_storeBytes, (nuint)offsetBytes + sizeBytes);
            _gl.BufferSubData(BufferTargetARB.ElementArrayBuffer, offsetBytes, sizeBytes, ptr);
        }
    }

    /// <summary>
    /// Uploads 32-bit unsigned integer indices into the GPU buffer.
    /// </summary>
    /// <param name="indices">The span of 32-bit indices.</param>
    /// <param name="offsetIndices">The index offset within the buffer.</param>
    /// <param name="discardBuffer">Whether to orphan the existing buffer backing to prevent GPU stalls.</param>
    public unsafe void SetData(ReadOnlySpan<uint> indices, uint offsetIndices = 0, bool discardBuffer = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (this.IndexType != DrawElementsType.UnsignedInt)
        {
            throw new InvalidOperationException("Index buffer was configured for 16-bit unsigned shorts.");
        }

        if (indices.IsEmpty)
        {
            return;
        }

        if (offsetIndices + (uint)indices.Length > this.IndexCount)
        {
            throw new ArgumentOutOfRangeException(nameof(indices), "Index data exceeds buffer capacity.");
        }

        this.Bind();

        if (discardBuffer && offsetIndices == 0)
        {
            BufferUsageARB glUsage = ResolveGlUsage(this.Usage);
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer, this.IndexCount * sizeof(uint), null, glUsage);
            _storeBytes = this.IndexCount * sizeof(uint);
        }

        fixed (uint* ptr = indices)
        {
            nuint sizeBytes = (nuint)(indices.Length * sizeof(uint));
            nint offsetBytes = (nint)(offsetIndices * sizeof(uint));
            EnsureStoreHolds(_storeBytes, (nuint)offsetBytes + sizeBytes);
            _gl.BufferSubData(BufferTargetARB.ElementArrayBuffer, offsetBytes, sizeBytes, ptr);
        }
    }

    /// <summary>
    /// Replaces the whole buffer with 32-bit <paramref name="indices"/>: the store is orphaned at their size and uploaded
    /// in one call (see <see cref="VertexBuffer{TVertex}.ReplaceData"/>). A later partial upload must stay inside them.
    /// </summary>
    /// <param name="indices">The new contents.</param>
    public unsafe void ReplaceData(ReadOnlySpan<uint> indices)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (this.IndexType != DrawElementsType.UnsignedInt)
        {
            throw new InvalidOperationException("Index buffer was configured for 16-bit unsigned shorts.");
        }

        if (indices.IsEmpty)
        {
            return;
        }

        if ((uint)indices.Length > this.IndexCount)
        {
            throw new ArgumentOutOfRangeException(nameof(indices), "Index data exceeds buffer capacity.");
        }

        this.Bind();
        fixed (uint* ptr = indices)
        {
            nuint sizeBytes = (nuint)(indices.Length * sizeof(uint));
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer, sizeBytes, ptr, ResolveGlUsage(this.Usage));
            _storeBytes = sizeBytes;
        }
    }

    // ReplaceData sizes the store to its data; a later partial upload must stay inside it. to its data; a later partial upload must stay inside it.
    private static void EnsureStoreHolds(nuint storeBytes, nuint endBytes)
    {
        if (endBytes > storeBytes)
        {
            throw new InvalidOperationException("A partial upload reaches past the data of the last ReplaceData; replace the whole buffer or upload with discardBuffer.");
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
            _stateCache.InvalidateIndexBuffer(this.Handle);
            _gl.DeleteBuffer(this.Handle);
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
