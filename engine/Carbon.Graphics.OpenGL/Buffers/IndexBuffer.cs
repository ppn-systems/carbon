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
    private readonly GlStateManager _stateManager;
    private bool _disposed;

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
    /// <param name="stateManager">The state cache manager.</param>
    /// <param name="capacity">The maximum number of indices this buffer can store.</param>
    /// <param name="indexType">The index element type (UnsignedShort or UnsignedInt).</param>
    /// <param name="usage">The expected buffer usage hint.</param>
    public unsafe IndexBuffer(
        GL gl,
        GlStateManager stateManager,
        uint capacity,
        DrawElementsType indexType = DrawElementsType.UnsignedShort,
        BufferUsage usage = BufferUsage.DynamicDraw)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
        ArgumentOutOfRangeException.ThrowIfZero(capacity, nameof(capacity));

        this.IndexCount = capacity;
        this.IndexType = indexType;
        this.Usage = usage;

        uint elementSize = indexType switch
        {
            DrawElementsType.UnsignedShort => sizeof(ushort),
            DrawElementsType.UnsignedInt => sizeof(uint),
            DrawElementsType.UnsignedByte => throw new NotImplementedException(),
            _ => throw new ArgumentException("Only UnsignedShort and UnsignedInt index types are supported.", nameof(indexType))
        };

        this.Handle = _gl.GenBuffer();
        _stateManager.BindIndexBuffer(this.Handle);

        BufferUsageARB glUsage = usage switch
        {
            BufferUsage.StaticDraw => BufferUsageARB.StaticDraw,
            BufferUsage.DynamicDraw => BufferUsageARB.DynamicDraw,
            BufferUsage.StreamDraw => BufferUsageARB.StreamDraw,
            _ => BufferUsageARB.DynamicDraw
        };

        _gl.BufferData(BufferTargetARB.ElementArrayBuffer, capacity * elementSize, null, glUsage);
    }

    /// <inheritdoc/>
    public void Bind()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _stateManager.BindIndexBuffer(this.Handle);
    }

    /// <summary>
    /// Uploads 16-bit unsigned short indices into the GPU buffer.
    /// </summary>
    /// <param name="indices">The span of 16-bit indices.</param>
    /// <param name="offsetIndices">The index offset within the buffer.</param>
    public unsafe void SetData(ReadOnlySpan<ushort> indices, uint offsetIndices = 0)
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

        fixed (ushort* ptr = indices)
        {
            nuint sizeBytes = (nuint)(indices.Length * sizeof(ushort));
            nint offsetBytes = (nint)(offsetIndices * sizeof(ushort));
            _gl.BufferSubData(BufferTargetARB.ElementArrayBuffer, offsetBytes, sizeBytes, ptr);
        }
    }

    /// <summary>
    /// Uploads 32-bit unsigned integer indices into the GPU buffer.
    /// </summary>
    /// <param name="indices">The span of 32-bit indices.</param>
    /// <param name="offsetIndices">The index offset within the buffer.</param>
    public unsafe void SetData(ReadOnlySpan<uint> indices, uint offsetIndices = 0)
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

        fixed (uint* ptr = indices)
        {
            nuint sizeBytes = (nuint)(indices.Length * sizeof(uint));
            nint offsetBytes = (nint)(offsetIndices * sizeof(uint));
            _gl.BufferSubData(BufferTargetARB.ElementArrayBuffer, offsetBytes, sizeBytes, ptr);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _gl.DeleteBuffer(this.Handle);
            _disposed = true;
        }
    }
}
