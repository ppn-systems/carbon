// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Graphics.Batching.Vertices;
using Carbon.Graphics.OpenGL.Abstractions;

namespace Carbon.Graphics.Batching.Sprites;

/// <summary>
/// Internal container holding pre-calculated quad vertices and sorting metadata for batch rendering.
/// </summary>
internal struct SpriteBatchItem
{
    /// <summary>
    /// Top-left quad vertex.
    /// </summary>
    public Vertex2D VertexTL;

    /// <summary>
    /// Top-right quad vertex.
    /// </summary>
    public Vertex2D VertexTR;

    /// <summary>
    /// Bottom-right quad vertex.
    /// </summary>
    public Vertex2D VertexBR;

    /// <summary>
    /// Bottom-left quad vertex.
    /// </summary>
    public Vertex2D VertexBL;

    /// <summary>
    /// The texture associated with this quad.
    /// </summary>
    public ITexture Texture;

    /// <summary>
    /// The calculated sort key for ordering.
    /// </summary>
    public float SortKey;
}
