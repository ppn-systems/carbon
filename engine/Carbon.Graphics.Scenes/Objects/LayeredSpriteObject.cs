// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Graphics.Animation.Atlas;
using Carbon.Graphics.Animation.Controllers;
using Carbon.Graphics.Animation.Directions;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Sprites;

namespace Carbon.Graphics.Scenes.Objects;

/// <summary>
/// Composite paperdoll visual object driven by <see cref="LayeredSpriteAnimator"/> for multi-layer 2.5D MMORPG entities (Body, Armor, Weapon, Wings).
/// All equipment layers share a single synchronized master playback timeline.
/// </summary>
public class LayeredSpriteObject : RenderObject
{
    private readonly LayeredSpriteAnimator _animator = new();

    /// <summary>
    /// Gets the composite layered animator driving master timeline and layer synchronization.
    /// </summary>
    public LayeredSpriteAnimator Animator => _animator;

    /// <summary>
    /// Gets or sets the 8-way facing direction across all synchronized equipment layers.
    /// </summary>
    public Direction8 Direction
    {
        get => _animator.Direction;
        set => _animator.SetDirection(value);
    }

    /// <summary>
    /// Gets or sets the explicit depth buffer value for 3D sorting.
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
            AtlasRegion region = _animator.MasterAnimator.CurrentFrame.Region;
            if (!region.IsValid)
            {
                return new RectangleF(this.Position.X, this.Position.Y, 0f, 0f);
            }

            float width = region.Bounds.Width * this.Scale.X;
            float height = region.Bounds.Height * this.Scale.Y;

            return new RectangleF(
                this.Position.X - (region.Origin.X * this.Scale.X),
                this.Position.Y - (region.Origin.Y * this.Scale.Y),
                width,
                height);
        }
    }

    /// <summary>
    /// Adds a new synchronized equipment or character layer to this composite entity.
    /// </summary>
    /// <param name="layer">The animation layer to attach.</param>
    public void AddLayer(AnimationLayer layer) =>
        _animator.AddLayer(layer);

    /// <summary>
    /// Removes an animation layer by its unique identifier name.
    /// </summary>
    /// <param name="name">The name of the layer to detach.</param>
    /// <returns><see langword="true"/> if removed; otherwise, <see langword="false"/>.</returns>
    public bool RemoveLayer(string name) =>
        _animator.RemoveLayer(name);

    /// <summary>
    /// Sets the 8-way facing direction across all layers.
    /// </summary>
    /// <param name="direction">The 8-way direction.</param>
    public void SetDirection(Direction8 direction) =>
        _animator.SetDirection(direction);

    /// <inheritdoc/>
    public override void Update(double dt)
    {
        if (this.IsEnabled)
        {
            _animator.Update((float)dt);
        }
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

        _animator.Draw(
            spriteBatch,
            this.Position,
            this.Color,
            this.Rotation,
            this.Scale,
            this.Depth,
            footY,
            this.Layer);
    }
}
