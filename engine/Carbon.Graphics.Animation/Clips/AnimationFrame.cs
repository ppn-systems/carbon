// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Animation.Atlas;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.OpenGL.Abstractions;

namespace Carbon.Graphics.Animation.Clips;

/// <summary>
/// Represents a single frame in a sprite animation sequence, specifying the visual region, duration, and metadata.
/// </summary>
public readonly struct AnimationFrame : IEquatable<AnimationFrame>
{
    /// <summary>
    /// Gets the texture atlas region rendered for this frame.
    /// </summary>
    public AtlasRegion Region { get; }

    /// <summary>
    /// Gets the duration of this frame in seconds.
    /// </summary>
    public float Duration { get; }

    /// <summary>
    /// Gets the optional event tag dispatched when this frame becomes active (e.g. "hit", "step", "cast").
    /// </summary>
    public string? EventTag { get; }

    /// <summary>
    /// Gets the positional offset applied to this frame relative to the sprite position.
    /// </summary>
    public Vector2 Offset { get; }

    /// <summary>
    /// Gets the visual flip transformation applied specifically to this frame.
    /// </summary>
    public SpriteEffects Effects { get; }

    /// <summary>
    /// Gets the width of this frame in pixels.
    /// </summary>
    public float Width => this.Region.Width;

    /// <summary>
    /// Gets the height of this frame in pixels.
    /// </summary>
    public float Height => this.Region.Height;

    /// <summary>
    /// Gets the underlying texture of this frame.
    /// </summary>
    public ITexture Texture => this.Region.Texture;

    /// <summary>
    /// Gets the pivot origin of this frame in pixels.
    /// </summary>
    public Vector2 Origin => this.Region.Origin;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnimationFrame"/> struct.
    /// </summary>
    /// <param name="region">The atlas region for this frame.</param>
    /// <param name="duration">Duration of the frame in seconds. Must be positive.</param>
    /// <param name="eventTag">Optional event tag triggered when this frame is reached.</param>
    /// <param name="offset">Optional positional offset vector.</param>
    /// <param name="effects">Optional sprite flip transformation.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="duration"/> is zero or negative.</exception>
    public AnimationFrame(
        AtlasRegion region,
        float duration,
        string? eventTag = null,
        Vector2? offset = null,
        SpriteEffects effects = SpriteEffects.None)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(duration, nameof(duration));

        this.Region = region;
        this.Duration = duration;
        this.EventTag = eventTag;
        this.Offset = offset ?? Vector2.Zero;
        this.Effects = effects;
    }

    /// <inheritdoc/>
    public bool Equals(AnimationFrame other) =>
        this.Region.Equals(other.Region)
        && Math.Abs(this.Duration - other.Duration) < 0.0001f
        && string.Equals(this.EventTag, other.EventTag, StringComparison.Ordinal)
        && this.Offset.Equals(other.Offset)
        && this.Effects == other.Effects;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is AnimationFrame other && this.Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(this.Region, this.Duration, this.EventTag, this.Offset, this.Effects);

    /// <summary>
    /// Compares two <see cref="AnimationFrame"/> instances for equality.
    /// </summary>
    public static bool operator ==(AnimationFrame left, AnimationFrame right) => left.Equals(right);

    /// <summary>
    /// Compares two <see cref="AnimationFrame"/> instances for inequality.
    /// </summary>
    public static bool operator !=(AnimationFrame left, AnimationFrame right) => !left.Equals(right);
}
