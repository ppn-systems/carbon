// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Buffers;
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

namespace Carbon.Graphics.Batching.Sprites;

/// <summary>
/// High-performance 2D/2.5D batch renderer optimizing sprite draws via dynamic quad streaming and Y-sorting.
/// </summary>
public sealed class SpriteBatch : IDisposable
{
    private const int MAX_BATCH_ITEMS = 4096;
    private const int VERTICES_PER_ITEM = 4;
    private const int INDICES_PER_ITEM = 6;
    private const int MAX_VERTICES = MAX_BATCH_ITEMS * VERTICES_PER_ITEM;
    private const int MAX_INDICES = MAX_BATCH_ITEMS * INDICES_PER_ITEM;

    private readonly IGraphicsDevice _device;
    private readonly GlStateCache _stateCache;
    private readonly VertexBuffer<Vertex2D> _vbo;
    private readonly IndexBuffer _ebo;
    private readonly VertexArray _vao;
    private readonly IShader _defaultShader;

    private readonly SpriteBatchItem[] _items = new SpriteBatchItem[MAX_BATCH_ITEMS];
    private readonly Vertex2D[] _vertexScratch = new Vertex2D[MAX_VERTICES];
    private int _itemCount;

    private bool _hasBegun;
    private DepthSortMode _sortMode;
    private BlendState _blendState;
    private Matrix4x4 _transformMatrix;
    private IShader _activeShader;
    private int _uViewProjLocation;
    private int _uTextureLocation;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpriteBatch"/> class.
    /// </summary>
    /// <param name="device">The graphics device used for rendering.</param>
    public SpriteBatch(IGraphicsDevice device)
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

        _defaultShader = BuiltinShaders.CreateSpriteShader(device.Gl, _stateCache);
        _activeShader = _defaultShader;

        _vbo = new VertexBuffer<Vertex2D>(device.Gl, _stateCache, MAX_VERTICES, BufferUsage.DynamicDraw);
        _ebo = new IndexBuffer(device.Gl, _stateCache, MAX_INDICES, DrawElementsType.UnsignedInt, BufferUsage.StaticDraw);
        _vao = new VertexArray(device.Gl, _stateCache);

        _vao.AttachVertexBuffer(_vbo, Vertex2D.Layout);
        _vao.AttachIndexBuffer(_ebo);

        this.InitializeIndexBuffer();
    }

    /// <summary>
    /// Begins a sprite batch rendering pass with specified sort mode, blending, transformation, and optional shader.
    /// </summary>
    /// <param name="sortMode">The depth and layer sorting strategy (e.g. YSorted for 2.5D).</param>
    /// <param name="blendState">The blend state, or null to use standard non-premultiplied alpha.</param>
    /// <param name="transformMatrix">Transformation matrix (typically from <see cref="Camera.Camera2D.ViewProjectionMatrix"/>).</param>
    /// <param name="customShader">An optional custom shader program to override the default sprite shader.</param>
    public void Begin(
        DepthSortMode sortMode = DepthSortMode.Immediate,
        BlendState? blendState = null,
        in Matrix4x4? transformMatrix = null,
        IShader? customShader = null)
    {
        if (_hasBegun)
        {
            throw new InvalidOperationException("SpriteBatch.Begin cannot be called again before End is called.");
        }

        _sortMode = sortMode;
        _blendState = blendState ?? BlendState.NonPremultiplied;
        _transformMatrix = transformMatrix ?? Matrix4x4.Identity;
        _activeShader = customShader ?? _defaultShader;

        _uViewProjLocation = _activeShader.GetUniformLocation("uViewProjection");
        _uTextureLocation = _activeShader.GetUniformLocation("uTexture");

        _hasBegun = true;
    }

    /// <summary>
    /// Enqueues a sprite for rendering with basic position and optional tint color.
    /// </summary>
    /// <param name="texture">The GPU texture to draw.</param>
    /// <param name="position">The top-left world position.</param>
    /// <param name="color">The tint color, or white if omitted.</param>
    public void Draw(ITexture texture, Vector2 position, ColorRgba? color = null) =>
        this.Draw(
            texture,
            position,
            null,
            color ?? ColorRgba.White,
            0f,
            Vector2.Zero,
            Vector2.One,
            SpriteEffects.None,
            0f,
            position.Y,
            0);

    /// <summary>
    /// Enqueues a sprite for rendering into a target destination rectangle.
    /// </summary>
    /// <param name="texture">The GPU texture to draw.</param>
    /// <param name="destinationRectangle">The world rectangle where the sprite will be drawn.</param>
    /// <param name="color">The tint color, or white if omitted.</param>
    public void Draw(ITexture texture, in RectangleF destinationRectangle, ColorRgba? color = null)
    {
        ArgumentNullException.ThrowIfNull(texture, nameof(texture));
        this.Draw(
            texture,
            new Vector2(destinationRectangle.X, destinationRectangle.Y),
            null,
            color ?? ColorRgba.White,
            0f,
            Vector2.Zero,
            new Vector2(destinationRectangle.Width / MathF.Max(1f, texture.Width), destinationRectangle.Height / MathF.Max(1f, texture.Height)),
            SpriteEffects.None,
            0f,
            destinationRectangle.Bottom,
            0);
    }

    /// <summary>
    /// Enqueues a sprite with complete 2.5D transformations including rotation, origin, scale, mirror effects, depth, and foot Y.
    /// </summary>
    /// <param name="texture">The GPU texture to draw.</param>
    /// <param name="position">The anchor position in world space.</param>
    /// <param name="sourceRectangle">Source pixel rectangle on the texture, or null for the full texture.</param>
    /// <param name="color">The tint color.</param>
    /// <param name="rotation">Rotation in radians.</param>
    /// <param name="origin">The rotation and scale pivot point in local texture pixels.</param>
    /// <param name="scale">Horizontal and vertical scale multipliers.</param>
    /// <param name="effects">Mirror flip effects.</param>
    /// <param name="depth">The Z depth value.</param>
    /// <param name="footY">The world Y position of the character's feet or base used for 2.5D Y-sorting.</param>
    /// <param name="layer">The explicit rendering layer index (higher layers draw in front).</param>
    public void Draw(
        ITexture texture,
        Vector2 position,
        RectangleF? sourceRectangle,
        ColorRgba color,
        float rotation,
        Vector2 origin,
        Vector2 scale,
        SpriteEffects effects = SpriteEffects.None,
        float depth = 0f,
        float footY = 0f,
        int layer = 0)
    {
        if (!_hasBegun)
        {
            throw new InvalidOperationException("SpriteBatch.Draw must be called between Begin and End.");
        }

        ArgumentNullException.ThrowIfNull(texture, nameof(texture));

        if (_itemCount >= MAX_BATCH_ITEMS)
        {
            this.Flush();
        }

        float texW = texture.Width;
        float texH = texture.Height;

        RectangleF src = sourceRectangle ?? new RectangleF(0f, 0f, texW, texH);

        // Normalized texture coordinates
        float u0 = src.X / texW;
        float v0 = src.Y / texH;
        float u1 = (src.X + src.Width) / texW;
        float v1 = (src.Y + src.Height) / texH;

        if ((effects & SpriteEffects.FlipHorizontally) != 0)
        {
            (u0, u1) = (u1, u0);
        }

        if ((effects & SpriteEffects.FlipVertically) != 0)
        {
            (v0, v1) = (v1, v0);
        }

        // Compute local quad corner offsets relative to origin
        float w = src.Width * scale.X;
        float h = src.Height * scale.Y;
        float ox = origin.X * scale.X;
        float oy = origin.Y * scale.Y;

        float p0x = -ox;
        float p0y = -oy;
        float p1x = w - ox;
        float p1y = -oy;
        float p2x = w - ox;
        float p2y = h - oy;
        float p3x = -ox;
        float p3y = h - oy;

        if (rotation != 0.0f)
        {
            float cos = MathF.Cos(rotation);
            float sin = MathF.Sin(rotation);

            float r0x = (p0x * cos) - (p0y * sin);
            float r0y = (p0x * sin) + (p0y * cos);
            float r1x = (p1x * cos) - (p1y * sin);
            float r1y = (p1x * sin) + (p1y * cos);
            float r2x = (p2x * cos) - (p2y * sin);
            float r2y = (p2x * sin) + (p2y * cos);
            float r3x = (p3x * cos) - (p3y * sin);
            float r3y = (p3x * sin) + (p3y * cos);

            p0x = r0x;
            p0y = r0y;
            p1x = r1x;
            p1y = r1y;
            p2x = r2x;
            p2y = r2y;
            p3x = r3x;
            p3y = r3y;
        }

        ref SpriteBatchItem item = ref _items[_itemCount++];
        uint packedColor = color.PackedValue;

        item.VertexTL = new Vertex2D(position.X + p0x, position.Y + p0y, depth, packedColor, u0, v0);
        item.VertexTR = new Vertex2D(position.X + p1x, position.Y + p1y, depth, packedColor, u1, v0);
        item.VertexBR = new Vertex2D(position.X + p2x, position.Y + p2y, depth, packedColor, u1, v1);
        item.VertexBL = new Vertex2D(position.X + p3x, position.Y + p3y, depth, packedColor, u0, v1);
        item.Texture = texture;

        float effectiveFootY = footY == 0f ? position.Y + p2y : footY;
        item.SortKey = SpriteSortKey.Calculate(_sortMode, effectiveFootY, layer, depth, texture.Handle);
    }

    /// <summary>
    /// Flushes all queued sprites to the GPU and renders the batch.
    /// </summary>
    public void Flush()
    {
        if (_itemCount == 0)
        {
            return;
        }

        _activeShader.Bind();
        if (_uViewProjLocation != -1)
        {
            _activeShader.SetUniform(_uViewProjLocation, in _transformMatrix);
        }

        if (_uTextureLocation != -1)
        {
            _activeShader.SetUniform(_uTextureLocation, 0);
        }

        _stateCache.SetBlendState(in _blendState);
        _vao.Bind();

        if (_sortMode == DepthSortMode.Immediate)
        {
            this.RenderUnsorted();
        }
        else
        {
            this.RenderSorted();
        }

        _itemCount = 0;
    }

    /// <summary>
    /// Ends the current sprite batch rendering pass, flushing all remaining sprites.
    /// </summary>
    public void End()
    {
        if (!_hasBegun)
        {
            throw new InvalidOperationException("SpriteBatch.End called without matching Begin.");
        }

        this.Flush();
        _hasBegun = false;
    }

    private void RenderUnsorted()
    {
        int batchStart = 0;
        ITexture? currentTexture = null;

        for (int i = 0; i < _itemCount; i++)
        {
            ITexture itemTexture = _items[i].Texture;

            if (currentTexture is null)
            {
                currentTexture = itemTexture;
            }
            else if (currentTexture.Handle != itemTexture.Handle)
            {
                this.DrawItemRange(batchStart, i - batchStart, currentTexture);
                currentTexture = itemTexture;
                batchStart = i;
            }

            int vBase = i * VERTICES_PER_ITEM;
            _vertexScratch[vBase + 0] = _items[i].VertexTL;
            _vertexScratch[vBase + 1] = _items[i].VertexTR;
            _vertexScratch[vBase + 2] = _items[i].VertexBR;
            _vertexScratch[vBase + 3] = _items[i].VertexBL;
        }

        if (currentTexture is not null && _itemCount > batchStart)
        {
            this.DrawItemRange(batchStart, _itemCount - batchStart, currentTexture);
        }
    }

    private void RenderSorted()
    {
        int[] rentedIndices = ArrayPool<int>.Shared.Rent(_itemCount);

        try
        {
            for (int i = 0; i < _itemCount; i++)
            {
                rentedIndices[i] = i;
            }

            Array.Sort(rentedIndices, 0, _itemCount, new SpriteItemComparer(_items));

            int batchStart = 0;
            ITexture? currentTexture = null;

            for (int i = 0; i < _itemCount; i++)
            {
                int itemIndex = rentedIndices[i];
                ITexture itemTexture = _items[itemIndex].Texture;

                if (currentTexture is null)
                {
                    currentTexture = itemTexture;
                }
                else if (currentTexture.Handle != itemTexture.Handle)
                {
                    this.DrawItemRange(batchStart, i - batchStart, currentTexture);
                    currentTexture = itemTexture;
                    batchStart = i;
                }

                int vBase = i * VERTICES_PER_ITEM;
                _vertexScratch[vBase + 0] = _items[itemIndex].VertexTL;
                _vertexScratch[vBase + 1] = _items[itemIndex].VertexTR;
                _vertexScratch[vBase + 2] = _items[itemIndex].VertexBR;
                _vertexScratch[vBase + 3] = _items[itemIndex].VertexBL;
            }

            if (currentTexture is not null && _itemCount > batchStart)
            {
                this.DrawItemRange(batchStart, _itemCount - batchStart, currentTexture);
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(rentedIndices);
        }
    }

    private void DrawItemRange(int itemStart, int itemCount, ITexture texture)
    {
        texture.Bind(0);

        int vertexStart = itemStart * VERTICES_PER_ITEM;
        int vertexCount = itemCount * VERTICES_PER_ITEM;
        uint indexCount = (uint)(itemCount * INDICES_PER_ITEM);

        _vbo.SetData(_vertexScratch.AsSpan(vertexStart, vertexCount), (uint)vertexStart, discardBuffer: itemStart == 0);

        _device.DrawElements(PrimitiveType.Triangles, indexCount, DrawElementsType.UnsignedInt, (nuint)(itemStart * INDICES_PER_ITEM * sizeof(uint)));
    }

    private void InitializeIndexBuffer()
    {
        uint[] indices = new uint[MAX_INDICES];
        for (uint i = 0, v = 0; i < MAX_INDICES; i += INDICES_PER_ITEM, v += VERTICES_PER_ITEM)
        {
            indices[i + 0] = v + 0;
            indices[i + 1] = v + 1;
            indices[i + 2] = v + 2;
            indices[i + 3] = v + 2;
            indices[i + 4] = v + 3;
            indices[i + 5] = v + 0;
        }

        _ebo.SetData(indices);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _vbo.Dispose();
            _ebo.Dispose();
            _vao.Dispose();
            _defaultShader.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    private readonly struct SpriteItemComparer : System.Collections.Generic.IComparer<int>
    {
        private readonly SpriteBatchItem[] _items;

        public SpriteItemComparer(SpriteBatchItem[] items) => _items = items;

        public int Compare(int x, int y) => _items[x].SortKey.CompareTo(_items[y].SortKey);
    }
}
