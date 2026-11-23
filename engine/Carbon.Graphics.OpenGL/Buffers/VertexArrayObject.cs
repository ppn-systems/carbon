using System;
using Carbon.Graphics.OpenGL.Abstractions;
using Carbon.Graphics.OpenGL.States;
using Silk.NET.OpenGL;

namespace Carbon.Graphics.OpenGL.Buffers;

/// <summary>
/// Encapsulates an OpenGL Vertex Array Object (VAO) that manages vertex buffer and attribute state.
/// </summary>
public sealed class VertexArrayObject : IDisposable
{
    private readonly GL _gl;
    private readonly GlStateManager _stateManager;
    private bool _disposed;

    /// <summary>
    /// Gets the OpenGL VAO handle.
    /// </summary>
    public uint Handle { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="VertexArrayObject"/> class.
    /// </summary>
    /// <param name="gl">The OpenGL API instance.</param>
    /// <param name="stateManager">The state cache manager.</param>
    public VertexArrayObject(GL gl, GlStateManager stateManager)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));

        this.Handle = _gl.GenVertexArray();
    }

    /// <summary>
    /// Binds this vertex array object to the OpenGL pipeline.
    /// </summary>
    public void Bind()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _stateManager.BindVertexArray(this.Handle);
    }

    /// <summary>
    /// Attaches a vertex buffer and configures vertex attribute pointers according to the specified layout.
    /// </summary>
    /// <param name="vertexBuffer">The vertex buffer providing the data.</param>
    /// <param name="layout">The layout describing the elements.</param>
    public unsafe void AttachVertexBuffer(IVertexBuffer vertexBuffer, VertexLayout layout)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(vertexBuffer, nameof(vertexBuffer));
        ArgumentNullException.ThrowIfNull(layout, nameof(layout));

        this.Bind();
        vertexBuffer.Bind();

        for (int i = 0; i < layout.Elements.Count; i++)
        {
            VertexElement element = layout.Elements[i];
            _gl.EnableVertexAttribArray(element.Location);
            _gl.VertexAttribPointer(
                element.Location,
                element.ComponentCount,
                element.Type,
                element.Normalized,
                layout.Stride,
                (void*)element.Offset);
        }
    }

    /// <summary>
    /// Attaches an index buffer to this VAO.
    /// </summary>
    /// <param name="indexBuffer">The index buffer to attach.</param>
    public void AttachIndexBuffer(IIndexBuffer indexBuffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(indexBuffer, nameof(indexBuffer));

        this.Bind();
        indexBuffer.Bind();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _gl.DeleteVertexArray(this.Handle);
            _disposed = true;
        }
    }
}
