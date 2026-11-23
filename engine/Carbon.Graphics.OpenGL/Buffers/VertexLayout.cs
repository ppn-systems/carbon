// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.ObjectModel;

namespace Carbon.Graphics.OpenGL.Buffers;

/// <summary>
/// Defines the complete memory layout of a vertex structure including stride and elements.
/// </summary>
public sealed class VertexLayout
{
    /// <summary>
    /// Gets the total byte size (stride) of a single vertex.
    /// </summary>
    public uint Stride { get; }

    /// <summary>
    /// Gets the read-only collection of vertex elements in this layout.
    /// </summary>
    public ReadOnlyCollection<VertexElement> Elements { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="VertexLayout"/> class.
    /// </summary>
    /// <param name="stride">The total byte size of a single vertex.</param>
    /// <param name="elements">The array of vertex elements describing the structure.</param>
    public VertexLayout(uint stride, VertexElement[] elements)
    {
        ArgumentOutOfRangeException.ThrowIfZero(stride, nameof(stride));
        ArgumentNullException.ThrowIfNull(elements, nameof(elements));

        this.Stride = stride;
        this.Elements = Array.AsReadOnly(elements);
    }
}
