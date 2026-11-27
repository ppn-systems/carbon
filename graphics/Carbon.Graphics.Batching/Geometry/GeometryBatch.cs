// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Shaders;
using Carbon.Graphics.Batching.Vertices;
using Carbon.Graphics.OpenGL.Abstractions;
using Carbon.Graphics.OpenGL.Buffers;
using Carbon.Graphics.OpenGL.Device;
using Carbon.Graphics.OpenGL.States;
using Silk.NET.OpenGL;
using VertexArray = Carbon.Graphics.OpenGL.Buffers.VertexArray;

namespace Carbon.Graphics.Batching.Geometry;

/// <summary>
/// Renders 2D primitives and procedural shapes (lines, rectangles, circles, polygons) optimized with dynamic vertex batching.
/// </summary>
public sealed class GeometryBatch : IDisposable
{
    // Sized for a dense forest view (trees, canopies, shadows, leaves and weather in one world batch) so the batch
    // does not split into extra draw calls; 32k vertices are 512 KB of client memory and VBO.
    private const int MAX_VERTICES = 32768;
    private const int MAX_INDICES = 65536;

    private readonly IGraphicsDevice _device;
    private readonly GlStateCache _stateCache;
    private readonly VertexBuffer<VertexColor> _vbo;
    private readonly IndexBuffer _ebo;
    private readonly VertexArray _vao;
    private readonly IShader _colorShader;

    private readonly VertexColor[] _vertices = new VertexColor[MAX_VERTICES];
    private readonly uint[] _indices = new uint[MAX_INDICES];
    private int _vertexCount;
    private int _indexCount;

    private bool _hasBegun;
    private BlendState _blendState;
    private Matrix4x4 _transformMatrix;
    private int _uViewProjLocation;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeometryBatch"/> class.
    /// </summary>
    /// <param name="device">The graphics device used for rendering.</param>
    public GeometryBatch(IGraphicsDevice device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));

        if (device is GraphicsDevice concreteDevice)
        {
            _stateCache = concreteDevice.StateCache;
        }
        else
        {
            throw new ArgumentException("The graphics device must be a valid GraphicsDevice instance.", nameof(device));
        }

        _stateCache.BindVertexArray(0);
        _colorShader = BuiltinShaders.CreateColorShader(device.Gl, _stateCache);
        _vbo = new VertexBuffer<VertexColor>(device.Gl, _stateCache, MAX_VERTICES, BufferUsage.DynamicDraw);
        _ebo = new IndexBuffer(device.Gl, _stateCache, MAX_INDICES, DrawElementsType.UnsignedInt, BufferUsage.DynamicDraw);
        _vao = new VertexArray(device.Gl, _stateCache);

        _vao.AttachVertexBuffer(_vbo, VertexColor.Layout);
        _vao.AttachIndexBuffer(_ebo);
        _stateCache.BindVertexArray(0);
    }

    /// <summary>
    /// Begins a geometry rendering pass.
    /// </summary>
    /// <param name="transformMatrix">Transformation matrix (typically from <see cref="Camera.Camera2D.ViewProjectionMatrix"/>).</param>
    /// <param name="blendState">Optional blend state, defaults to non-premultiplied alpha.</param>
    public void Begin(in Matrix4x4? transformMatrix = null, BlendState? blendState = null)
    {
        if (_hasBegun)
        {
            throw new InvalidOperationException("GeometryBatch.Begin cannot be called again before End is called.");
        }

        _transformMatrix = transformMatrix ?? Matrix4x4.Identity;
        _blendState = blendState ?? BlendState.NonPremultiplied;
        _uViewProjLocation = _colorShader.GetUniformLocation("uViewProjection");

        _hasBegun = true;
    }

    /// <summary>
    /// Draws a line segment between two world positions with configurable thickness.
    /// </summary>
    /// <param name="start">Start world coordinate.</param>
    /// <param name="end">End world coordinate.</param>
    /// <param name="color">Line color.</param>
    /// <param name="thickness">Line thickness in world units.</param>
    /// <param name="depth">Depth Z coordinate.</param>
    public void DrawLine(Vector2 start, Vector2 end, ColorRgba color, float thickness = 1f, float depth = 0f)
    {
        Vector2 delta = end - start;
        float length = delta.Length();

        if (length <= 0.00001f)
        {
            return;
        }

        Vector2 normal = new(-delta.Y / length, delta.X / length);
        Vector2 halfNormal = normal * (thickness * 0.5f);

        Vector2 p0 = start + halfNormal;
        Vector2 p1 = end + halfNormal;
        Vector2 p2 = end - halfNormal;
        Vector2 p3 = start - halfNormal;

        this.AddQuad(p0, p1, p2, p3, color, depth);
    }

    /// <summary>
    /// Draws an outlined rectangle.
    /// </summary>
    /// <param name="rect">The rectangle bounds.</param>
    /// <param name="color">Outline color.</param>
    /// <param name="thickness">Border thickness.</param>
    /// <param name="depth">Depth coordinate.</param>
    public void DrawRectangle(in RectangleF rect, ColorRgba color, float thickness = 1f, float depth = 0f)
    {
        Vector2 tl = new(rect.Left, rect.Top);
        Vector2 tr = new(rect.Right, rect.Top);
        Vector2 br = new(rect.Right, rect.Bottom);
        Vector2 bl = new(rect.Left, rect.Bottom);

        this.DrawLine(tl, tr, color, thickness, depth);
        this.DrawLine(tr, br, color, thickness, depth);
        this.DrawLine(br, bl, color, thickness, depth);
        this.DrawLine(bl, tl, color, thickness, depth);
    }

    /// <summary>
    /// Draws a solid filled rectangle.
    /// </summary>
    /// <param name="rect">The rectangle bounds.</param>
    /// <param name="color">Fill color.</param>
    /// <param name="depth">Depth coordinate.</param>
    public void FillRectangle(in RectangleF rect, ColorRgba color, float depth = 0f)
    {
        Vector2 tl = new(rect.Left, rect.Top);
        Vector2 tr = new(rect.Right, rect.Top);
        Vector2 br = new(rect.Right, rect.Bottom);
        Vector2 bl = new(rect.Left, rect.Bottom);

        this.AddQuad(tl, tr, br, bl, color, depth);
    }

    /// <summary>
    /// Draws a solid filled triangle.
    /// </summary>
    /// <param name="p0">First vertex coordinate.</param>
    /// <param name="p1">Second vertex coordinate.</param>
    /// <param name="p2">Third vertex coordinate.</param>
    /// <param name="color">Fill color.</param>
    /// <param name="depth">Depth coordinate.</param>
    public void FillTriangle(Vector2 p0, Vector2 p1, Vector2 p2, ColorRgba color, float depth = 0f)
    {
        this.EnsureCapacity(3, 3);

        uint baseIndex = (uint)_vertexCount;
        uint packedColor = color.PackedValue;

        _vertices[_vertexCount++] = new VertexColor(p0.X, p0.Y, depth, packedColor);
        _vertices[_vertexCount++] = new VertexColor(p1.X, p1.Y, depth, packedColor);
        _vertices[_vertexCount++] = new VertexColor(p2.X, p2.Y, depth, packedColor);

        _indices[_indexCount++] = baseIndex;
        _indices[_indexCount++] = baseIndex + 1;
        _indices[_indexCount++] = baseIndex + 2;
    }

    /// <summary>
    /// Draws an outlined circle.
    /// </summary>
    /// <param name="center">Circle center in world space.</param>
    /// <param name="radius">Circle radius.</param>
    /// <param name="color">Outline color.</param>
    /// <param name="segments">Number of line segments.</param>
    /// <param name="thickness">Line thickness.</param>
    /// <param name="depth">Depth coordinate.</param>
    public void DrawCircle(Vector2 center, float radius, ColorRgba color, int segments = 32, float thickness = 1f, float depth = 0f)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(segments, 3, nameof(segments));

        float angleStep = MathF.PI * 2.0f / segments;
        Vector2 previousPoint = center + new Vector2(radius, 0f);

        for (int i = 1; i <= segments; i++)
        {
            float angle = i * angleStep;
            Vector2 currentPoint = center + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);
            this.DrawLine(previousPoint, currentPoint, color, thickness, depth);
            previousPoint = currentPoint;
        }
    }

    /// <summary>
    /// Draws a solid filled circle.
    /// </summary>
    /// <param name="center">Circle center in world space.</param>
    /// <param name="radius">Circle radius.</param>
    /// <param name="color">Fill color.</param>
    /// <param name="segments">Number of triangle segments.</param>
    /// <param name="depth">Depth coordinate.</param>
    public void FillCircle(Vector2 center, float radius, ColorRgba color, int segments = 32, float depth = 0f)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(segments, 3, nameof(segments));

        this.EnsureCapacity(segments + 1, segments * 3);

        uint packedColor = color.PackedValue;
        uint centerIndex = (uint)_vertexCount;
        _vertices[_vertexCount++] = new VertexColor(center.X, center.Y, depth, packedColor);

        float angleStep = MathF.PI * 2.0f / segments;
        uint firstPerimeterIndex = (uint)_vertexCount;

        for (int i = 0; i < segments; i++)
        {
            float angle = i * angleStep;
            float px = center.X + (MathF.Cos(angle) * radius);
            float py = center.Y + (MathF.Sin(angle) * radius);
            _vertices[_vertexCount++] = new VertexColor(px, py, depth, packedColor);
        }

        for (uint i = 0; i < (uint)segments; i++)
        {
            uint next = (i + 1) % (uint)segments;
            _indices[_indexCount++] = centerIndex;
            _indices[_indexCount++] = firstPerimeterIndex + i;
            _indices[_indexCount++] = firstPerimeterIndex + next;
        }
    }

    /// <summary>
    /// Draws a filled circle whose colour blends from <paramref name="innerColor"/> at the centre to
    /// <paramref name="outerColor"/> at the rim (soft lights, glows and contact shadows in a single fan).
    /// </summary>
    /// <param name="center">Circle center in world space.</param>
    /// <param name="radius">Circle radius.</param>
    /// <param name="innerColor">Colour at the centre.</param>
    /// <param name="outerColor">Colour at the rim (usually the same colour with zero alpha).</param>
    /// <param name="segments">Number of triangle segments.</param>
    /// <param name="scaleY">Vertical scale of the circle (below 1 for ground ellipses seen at an angle).</param>
    /// <param name="depth">Depth coordinate.</param>
    public void FillRadialGradient(Vector2 center, float radius, ColorRgba innerColor, ColorRgba outerColor, int segments = 24, float scaleY = 1f, float depth = 0f)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(segments, 3, nameof(segments));

        this.EnsureCapacity(segments + 1, segments * 3);

        uint rimColor = outerColor.PackedValue;
        uint centerIndex = (uint)_vertexCount;
        _vertices[_vertexCount++] = new VertexColor(center.X, center.Y, depth, innerColor.PackedValue);

        float angleStep = MathF.PI * 2.0f / segments;
        uint firstPerimeterIndex = (uint)_vertexCount;
        for (int i = 0; i < segments; i++)
        {
            float angle = i * angleStep;
            _vertices[_vertexCount++] = new VertexColor(center.X + (MathF.Cos(angle) * radius), center.Y + (MathF.Sin(angle) * radius * scaleY), depth, rimColor);
        }

        for (uint i = 0; i < (uint)segments; i++)
        {
            uint next = (i + 1) % (uint)segments;
            _indices[_indexCount++] = centerIndex;
            _indices[_indexCount++] = firstPerimeterIndex + i;
            _indices[_indexCount++] = firstPerimeterIndex + next;
        }
    }

    /// <summary>
    /// Draws a solid filled convex polygon (e.g. procedural biome or cavern polygon).
    /// </summary>
    /// <param name="points">Span of 2D convex polygon vertices in clockwise or counter-clockwise order.</param>
    /// <param name="color">Fill color.</param>
    /// <param name="depth">Depth coordinate.</param>
    public void FillConvexPolygon(ReadOnlySpan<Vector2> points, ColorRgba color, float depth = 0f)
    {
        if (points.Length < 3)
        {
            return;
        }

        int triangleCount = points.Length - 2;
        this.EnsureCapacity(points.Length, triangleCount * 3);

        uint baseIndex = (uint)_vertexCount;
        uint packedColor = color.PackedValue;

        for (int i = 0; i < points.Length; i++)
        {
            _vertices[_vertexCount++] = new VertexColor(points[i].X, points[i].Y, depth, packedColor);
        }

        for (uint i = 1; i < (uint)(points.Length - 1); i++)
        {
            _indices[_indexCount++] = baseIndex;
            _indices[_indexCount++] = baseIndex + i;
            _indices[_indexCount++] = baseIndex + i + 1;
        }
    }

    /// <summary>
    /// Flushes all queued geometry to the GPU.
    /// </summary>
    public void Flush()
    {
        if (_vertexCount == 0 || _indexCount == 0)
        {
            return;
        }

        _colorShader.Bind();
        if (_uViewProjLocation != -1)
        {
            _colorShader.SetUniform(_uViewProjLocation, in _transformMatrix);
        }

        _stateCache.SetBlendState(in _blendState);
        _stateCache.SetDepthStencilState(DepthStencilState.None);
        _vao.Bind();

        _vbo.SetData(_vertices.AsSpan(0, _vertexCount), 0, discardBuffer: true);
        _ebo.SetData(_indices.AsSpan(0, _indexCount), 0, discardBuffer: true);

        _device.DrawElements(PrimitiveType.Triangles, (uint)_indexCount, DrawElementsType.UnsignedInt);

        _vertexCount = 0;
        _indexCount = 0;
    }

    /// <summary>
    /// Ends the geometry batching pass and flushes queued primitives.
    /// </summary>
    public void End()
    {
        if (!_hasBegun)
        {
            throw new InvalidOperationException("GeometryBatch.End called without matching Begin.");
        }

        this.Flush();
        _hasBegun = false;
    }

    private void AddQuad(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, ColorRgba color, float depth)
    {
        this.EnsureCapacity(4, 6);

        uint baseIndex = (uint)_vertexCount;
        uint packedColor = color.PackedValue;

        _vertices[_vertexCount++] = new VertexColor(p0.X, p0.Y, depth, packedColor);
        _vertices[_vertexCount++] = new VertexColor(p1.X, p1.Y, depth, packedColor);
        _vertices[_vertexCount++] = new VertexColor(p2.X, p2.Y, depth, packedColor);
        _vertices[_vertexCount++] = new VertexColor(p3.X, p3.Y, depth, packedColor);

        _indices[_indexCount++] = baseIndex + 0;
        _indices[_indexCount++] = baseIndex + 1;
        _indices[_indexCount++] = baseIndex + 2;
        _indices[_indexCount++] = baseIndex + 2;
        _indices[_indexCount++] = baseIndex + 3;
        _indices[_indexCount++] = baseIndex + 0;
    }

    private void EnsureCapacity(int neededVertices, int neededIndices)
    {
        if (_vertexCount + neededVertices > MAX_VERTICES || _indexCount + neededIndices > MAX_INDICES)
        {
            this.Flush();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _vbo.Dispose();
            _ebo.Dispose();
            _vao.Dispose();
            _colorShader.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
