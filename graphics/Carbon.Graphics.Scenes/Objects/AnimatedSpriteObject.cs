// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Graphics.Animation.Atlas;
using Carbon.Graphics.Animation.Clips;
using Carbon.Graphics.Animation.Controllers;
using Carbon.Graphics.Animation.Directions;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Sprites;

namespace Carbon.Graphics.Scenes.Objects;

/// <summary>
/// Dynamic sprite visual object driven by <see cref="SpriteAnimator"/> with support for 2.5D 4-way and 8-way directional clips.
/// </summary>
public class AnimatedSpriteObject : RenderObject
{
    private readonly SpriteAnimator _animator = new();

    /// <summary>
    /// Gets the underlying animator driving frame sequencing, timing, and events.
    /// </summary>
    public SpriteAnimator Animator => _animator;

    /// <summary>
    /// Gets or sets the 8-way facing direction.
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
            AtlasRegion region = _animator.CurrentFrame.Region;
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
    /// Initializes a new instance of the <see cref="AnimatedSpriteObject"/> class.
    /// </summary>
    public AnimatedSpriteObject()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AnimatedSpriteObject"/> class playing the specified animation clip.
    /// </summary>
    /// <param name="clip">The animation clip to play.</param>
    public AnimatedSpriteObject(AnimationClip clip)
    {
        ArgumentNullException.ThrowIfNull(clip, nameof(clip));
        _animator.Play(clip);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AnimatedSpriteObject"/> class playing a directional clip in the specified direction.
    /// </summary>
    /// <param name="directionalClip">The multi-direction clip to play.</param>
    /// <param name="initialDirection">The initial facing heading.</param>
    public AnimatedSpriteObject(DirectionalAnimationClip directionalClip, Direction8 initialDirection = Direction8.South)
    {
        ArgumentNullException.ThrowIfNull(directionalClip, nameof(directionalClip));
        _animator.Play(directionalClip, initialDirection);
    }

    /// <summary>
    /// Plays a non-directional animation clip.
    /// </summary>
    /// <param name="clip">The clip to play.</param>
    /// <param name="restart">Whether to force playback restart if already playing.</param>
    public void Play(AnimationClip clip, bool restart = false) =>
        _animator.Play(clip, restart);

    /// <summary>
    /// Plays a directional animation clip.
    /// </summary>
    /// <param name="directionalClip">The clip containing direction variants.</param>
    /// <param name="direction">The facing direction.</param>
    /// <param name="restart">Whether to force playback restart if already playing.</param>
    public void Play(DirectionalAnimationClip directionalClip, Direction8 direction, bool restart = false) =>
        _animator.Play(directionalClip, direction, restart);

    /// <summary>
    /// Sets the facing direction using 4-way cardinal compass directions.
    /// </summary>
    /// <param name="direction">The 4-way direction.</param>
    public void SetDirection(Direction4 direction) =>
        _animator.SetDirection(direction switch
        {
            Direction4.South => Direction8.South,
            Direction4.East => Direction8.East,
            Direction4.North => Direction8.North,
            Direction4.West => Direction8.West,
            _ => Direction8.South
        });

    /// <summary>
    /// Sets the facing direction using 8-way compass directions.
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
