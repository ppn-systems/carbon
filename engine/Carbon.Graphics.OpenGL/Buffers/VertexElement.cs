// Copyright (c) 2026 PPN Systems. All rights reserved.

using Silk.NET.OpenGL;

namespace Carbon.Graphics.OpenGL.Buffers;

/// <summary>
/// Defines a single element within a vertex layout (e.g. Position, Color, TextureCoordinates).
/// </summary>
/// <param name="Location">The shader attribute location index (e.g. layout(location = 0)).</param>
/// <param name="ComponentCount">The number of components per vertex element (1, 2, 3, or 4).</param>
/// <param name="Type">The OpenGL vertex attribute pointer data type.</param>
/// <param name="Normalized">Whether fixed-point data values should be normalized to [-1, 1] or [0, 1].</param>
/// <param name="Offset">The byte offset of this element from the beginning of the vertex.</param>
public readonly record struct VertexElement(uint Location, int ComponentCount, VertexAttribPointerType Type, bool Normalized, uint Offset);
