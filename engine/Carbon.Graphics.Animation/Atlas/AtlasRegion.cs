// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.OpenGL.Abstractions;

namespace Carbon.Graphics.Animation.Atlas;

/// <summary>
/// Represents a named rectangular sub-region within an <see cref="ITexture"/>, with pivot origin and trimming metadata.
/// </summary>
public readonly struct AtlasRegion : IEquatable<AtlasRegion>
{
    /// <summary>
    /// Gets the source texture containing this region.
    /// </summary>
    public ITexture Texture { get; }

    /// <summary>
    /// Gets the rectangular pixel boundary of this region on the source texture.
    /// </summary>
    public RectangleF Bounds { get; }

    /// <summary>
    /// Gets the pivot origin point in pixels relative to the top-left of the region.
    /// </summary>
    public Vector2 Origin { get; }

    /// <summary>
    /// Gets the unique identifier or name of this region.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the untrimmed original size of the sprite in pixels, or the bounds size if untrimmed.
    /// </summary>
    public Vector2 OriginalSize { get; }

    /// <summary>
    /// Gets the offset from the original sprite's top-left corner caused by trimming transparent pixels.
    /// </summary>
    public Vector2 TrimOffset { get; }

    /// <summary>
    /// Gets the width of this region in pixels.
    /// </summary>
    public float Width => this.Bounds.Width;

    /// <summary>
    /// Gets the height of this region in pixels.
    /// </summary>
    public float Height => this.Bounds.Height;

    /// <summary>
    /// Gets a value indicating whether this region represents a valid non-empty texture slice.
    /// </summary>
    public bool IsValid => this.Texture != null && this.Bounds.Width > 0f && this.Bounds.Height > 0f;

    /// <summary>
    /// Represents an empty, uninitialized atlas region.
    /// </summary>
    public static readonly AtlasRegion Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="AtlasRegion"/> struct.
    /// </summary>
    /// <param name="texture">The underlying source texture.</param>
    /// <param name="bounds">The pixel bounds on the texture.</param>
    /// <param name="name">The name or identifier of this region.</param>
    /// <param name="origin">The pivot origin in pixels. Defaults to (0, 0) if <see langword="null"/>.</param>
    /// <param name="originalSize">The original untrimmed size. Defaults to bounds size if <see langword="null"/>.</param>
    /// <param name="trimOffset">The trim offset vector. Defaults to (0, 0) if <see langword="null"/>.</param>
    public AtlasRegion(
        ITexture texture,
        in RectangleF bounds,
        string name = "",
        Vector2? origin = null,
        Vector2? originalSize = null,
        Vector2? trimOffset = null)
    {
        this.Texture = texture;
        this.Bounds = bounds;
        this.Name = name ?? string.Empty;
        this.Origin = origin ?? Vector2.Zero;
        this.OriginalSize = originalSize ?? new Vector2(bounds.Width, bounds.Height);
        this.TrimOffset = trimOffset ?? Vector2.Zero;
    }

    /// <summary>
    /// Creates a copy of this region with an updated pivot origin.
    /// </summary>
    /// <param name="origin">The new pivot origin in pixels.</param>
    /// <returns>A new <see cref="AtlasRegion"/> instance with the specified origin.</returns>
    public AtlasRegion WithOrigin(Vector2 origin) =>
        new(this.Texture, this.Bounds, this.Name, origin, this.OriginalSize, this.TrimOffset);

    /// <summary>
    /// Creates a copy of this region with the pivot origin set to the bottom-center (ideal for 2.5D feet Y-sorting).
    /// </summary>
    /// <returns>A new <see cref="AtlasRegion"/> instance with origin at bottom-center.</returns>
    public AtlasRegion WithBottomCenterOrigin() =>
        new(
            this.Texture,
            this.Bounds,
            this.Name,
            new Vector2(this.Bounds.Width * 0.5f, this.Bounds.Height),
            this.OriginalSize,
            this.TrimOffset);

    /// <inheritdoc/>
    public bool Equals(AtlasRegion other) =>
        ReferenceEquals(this.Texture, other.Texture)
        && this.Bounds.Equals(other.Bounds)
        && this.Origin.Equals(other.Origin)
        && string.Equals(this.Name, other.Name, StringComparison.Ordinal)
        && this.OriginalSize.Equals(other.OriginalSize)
        && this.TrimOffset.Equals(other.TrimOffset);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is AtlasRegion other && this.Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(this.Texture, this.Bounds, this.Origin, this.Name);

    /// <summary>
    /// Compares two <see cref="AtlasRegion"/> instances for equality.
    /// </summary>
    public static bool operator ==(AtlasRegion left, AtlasRegion right) => left.Equals(right);

    /// <summary>
    /// Compares two <see cref="AtlasRegion"/> instances for inequality.
    /// </summary>
    public static bool operator !=(AtlasRegion left, AtlasRegion right) => !left.Equals(right);

    /// <inheritdoc/>
    public override string ToString() =>
        $"AtlasRegion '{this.Name}' [{this.Bounds.X}, {this.Bounds.Y}, {this.Bounds.Width}x{this.Bounds.Height}]";
}
