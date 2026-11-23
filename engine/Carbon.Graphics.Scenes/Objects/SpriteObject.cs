// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.OpenGL.Abstractions;

namespace Carbon.Graphics.Scenes.Objects;

/// <summary>
/// Static sprite visual object rendering a GPU texture or sub-rectangle with 2.5D Y-sorting.
/// </summary>
public class SpriteObject : RenderObject
{
    private ITexture _texture;
    private RectangleF? _sourceRectangle;

    /// <summary>
    /// Gets or sets the GPU texture to render.
    /// </summary>
    public ITexture Texture
    {
        get => _texture;
        set => _texture = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Gets or sets the source texture sub-rectangle in pixels, or null for the full texture.
    /// </summary>
    public RectangleF? SourceRectangle
    {
        get => _sourceRectangle;
        set => _sourceRectangle = value;
    }

    /// <summary>
    /// Gets or sets horizontal and vertical mirror flipping effects.
    /// </summary>
    public SpriteEffects Effects { get; set; } = SpriteEffects.None;

    /// <summary>
    /// Gets or sets the depth buffer value for 3D/Z sorting passes.
    /// </summary>
    public float Depth { get; set; }

    /// <summary>
    /// Gets or sets the explicit feet Y position for 2.5D depth ordering (0 to default to Position.Y).
    /// </summary>
    public float FootY { get; set; }

    /// <inheritdoc/>
    public override RectangleF Bounds
    {
        get
        {
            float width = _sourceRectangle?.Width ?? _texture.Width;
            float height = _sourceRectangle?.Height ?? _texture.Height;
            float scaledW = width * this.Scale.X;
            float scaledH = height * this.Scale.Y;

            return new RectangleF(
                this.Position.X - (this.Origin.X * this.Scale.X),
                this.Position.Y - (this.Origin.Y * this.Scale.Y),
                scaledW,
                scaledH);
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SpriteObject"/> class.
    /// </summary>
    /// <param name="texture">The GPU texture to display.</param>
    public SpriteObject(ITexture texture) =>
        _texture = texture ?? throw new ArgumentNullException(nameof(texture));

    /// <summary>
    /// Initializes a new instance of the <see cref="SpriteObject"/> class with a specific source rectangle.
    /// </summary>
    /// <param name="texture">The GPU texture to display.</param>
    /// <param name="sourceRectangle">The sub-rectangle on the texture.</param>
    public SpriteObject(ITexture texture, in RectangleF sourceRectangle)
    {
        _texture = texture ?? throw new ArgumentNullException(nameof(texture));
        _sourceRectangle = sourceRectangle;
    }

    /// <inheritdoc/>
    public override void Draw(SpriteBatch spriteBatch)
    {
        ArgumentNullException.ThrowIfNull(spriteBatch, nameof(spriteBatch));

        if (!this.IsVisible)
        {
            return;
        }

        float footY = this.FootY == 0f ? this.Position.Y : this.FootY;

        spriteBatch.Draw(
            _texture,
            this.Position,
            _sourceRectangle,
            this.Color,
            this.Rotation,
            this.Origin,
            this.Scale,
            this.Effects,
            this.Depth,
            footY,
            this.Layer);
    }
}
