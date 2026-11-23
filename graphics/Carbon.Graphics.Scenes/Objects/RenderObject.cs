// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Scenes.Abstractions;

namespace Carbon.Graphics.Scenes.Objects;

/// <summary>
/// Abstract base class for scene renderable visual objects providing transform, layering, and lifecycle state.
/// </summary>
public abstract class RenderObject : IRenderObject
{
    /// <inheritdoc/>
    public Vector2 Position { get; set; }

    /// <inheritdoc/>
    public Vector2 Scale { get; set; } = Vector2.One;

    /// <inheritdoc/>
    public float Rotation { get; set; }

    /// <inheritdoc/>
    public Vector2 Origin { get; set; }

    /// <inheritdoc/>
    public ColorRgba Color { get; set; } = ColorRgba.White;

    /// <inheritdoc/>
    public int Layer { get; set; }

    /// <inheritdoc/>
    public int ZIndex { get; set; }

    /// <inheritdoc/>
    public bool IsVisible { get; set; } = true;

    /// <inheritdoc/>
    public bool IsEnabled { get; set; } = true;

    /// <inheritdoc/>
    public string? Tag { get; set; }

    /// <inheritdoc/>
    public abstract RectangleF Bounds { get; }

    /// <inheritdoc/>
    public virtual void Update(double dt)
    {
    }

    /// <inheritdoc/>
    public abstract void Draw(SpriteBatch spriteBatch);
}
