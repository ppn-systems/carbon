// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Graphics.Animation.Directions;
using Carbon.Graphics.Batching.Common;

namespace Carbon.Graphics.Animation.Clips;

/// <summary>
/// Represents a multi-directional animation clip that maps 8-way (or 4-way) headings to directional <see cref="AnimationClip"/> instances.
/// Supports automated horizontal mirroring for classic 5-direction 2.5D asset pipelines.
/// </summary>
public sealed class DirectionalAnimationClip
{
    private readonly DirectionalVariant[] _variants;

    /// <summary>
    /// Gets the name of this directional animation action (e.g. "idle", "walk", "attack").
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DirectionalAnimationClip"/> class with all 8 explicit directions.
    /// </summary>
    /// <param name="name">The action name.</param>
    /// <param name="south">South facing clip.</param>
    /// <param name="southEast">SouthEast facing clip.</param>
    /// <param name="east">East facing clip.</param>
    /// <param name="northEast">NorthEast facing clip.</param>
    /// <param name="north">North facing clip.</param>
    /// <param name="northWest">NorthWest facing clip.</param>
    /// <param name="west">West facing clip.</param>
    /// <param name="southWest">SouthWest facing clip.</param>
    public DirectionalAnimationClip(
        string name,
        AnimationClip south,
        AnimationClip southEast,
        AnimationClip east,
        AnimationClip northEast,
        AnimationClip north,
        AnimationClip northWest,
        AnimationClip west,
        AnimationClip southWest)
    {
        ArgumentException.ThrowIfNullOrEmpty(name, nameof(name));
        ArgumentNullException.ThrowIfNull(south, nameof(south));
        ArgumentNullException.ThrowIfNull(southEast, nameof(southEast));
        ArgumentNullException.ThrowIfNull(east, nameof(east));
        ArgumentNullException.ThrowIfNull(northEast, nameof(northEast));
        ArgumentNullException.ThrowIfNull(north, nameof(north));
        ArgumentNullException.ThrowIfNull(northWest, nameof(northWest));
        ArgumentNullException.ThrowIfNull(west, nameof(west));
        ArgumentNullException.ThrowIfNull(southWest, nameof(southWest));

        this.Name = name;
        _variants =
        [
            new(south, SpriteEffects.None),
            new(southEast, SpriteEffects.None),
            new(east, SpriteEffects.None),
            new(northEast, SpriteEffects.None),
            new(north, SpriteEffects.None),
            new(northWest, SpriteEffects.None),
            new(west, SpriteEffects.None),
            new(southWest, SpriteEffects.None)
        ];
    }

    private DirectionalAnimationClip(string name, DirectionalVariant[] variants)
    {
        this.Name = name;
        _variants = variants;
    }

    /// <summary>
    /// Retrieves the animation clip and necessary flip transformation for the specified 8-way direction.
    /// </summary>
    /// <param name="direction">The facing direction.</param>
    /// <param name="flipEffects">Output sprite flip transformation (e.g. Horizontal Flip when mirrored).</param>
    /// <returns>The active <see cref="AnimationClip"/> for that direction.</returns>
    public AnimationClip GetClip(Direction8 direction, out SpriteEffects flipEffects)
    {
        int index = Math.Clamp((int)direction, 0, 7);
        DirectionalVariant variant = _variants[index];
        flipEffects = variant.Effects;
        return variant.Clip;
    }

    /// <summary>
    /// Creates a 5-directional animation clip that automatically mirrors East, NorthEast, and SouthEast to produce West, NorthWest, and SouthWest.
    /// This drastically reduces asset size and artist workload for 2.5D MMORPGs.
    /// </summary>
    /// <param name="name">The action name.</param>
    /// <param name="south">South facing clip.</param>
    /// <param name="southEast">SouthEast facing clip.</param>
    /// <param name="east">East facing clip.</param>
    /// <param name="northEast">NorthEast facing clip.</param>
    /// <param name="north">North facing clip.</param>
    /// <returns>A new <see cref="DirectionalAnimationClip"/> instance.</returns>
    public static DirectionalAnimationClip Create5Direction(
        string name,
        AnimationClip south,
        AnimationClip southEast,
        AnimationClip east,
        AnimationClip northEast,
        AnimationClip north)
    {
        ArgumentException.ThrowIfNullOrEmpty(name, nameof(name));
        ArgumentNullException.ThrowIfNull(south, nameof(south));
        ArgumentNullException.ThrowIfNull(southEast, nameof(southEast));
        ArgumentNullException.ThrowIfNull(east, nameof(east));
        ArgumentNullException.ThrowIfNull(northEast, nameof(northEast));
        ArgumentNullException.ThrowIfNull(north, nameof(north));

        DirectionalVariant[] variants =
        [
            new(south, SpriteEffects.None),
            new(southEast, SpriteEffects.None),
            new(east, SpriteEffects.None),
            new(northEast, SpriteEffects.None),
            new(north, SpriteEffects.None),
            new(northEast, SpriteEffects.FlipHorizontally),
            new(east, SpriteEffects.FlipHorizontally),
            new(southEast, SpriteEffects.FlipHorizontally)
        ];

        return new DirectionalAnimationClip(name, variants);
    }

    /// <summary>
    /// Creates a 4-directional animation clip for cardinal orientations (South, West, East, North).
    /// Diagonal headings automatically map to the closest cardinal clip.
    /// </summary>
    /// <param name="name">The action name.</param>
    /// <param name="south">South facing clip.</param>
    /// <param name="west">West facing clip.</param>
    /// <param name="east">East facing clip.</param>
    /// <param name="north">North facing clip.</param>
    /// <returns>A new <see cref="DirectionalAnimationClip"/> instance.</returns>
    public static DirectionalAnimationClip Create4Direction(
        string name,
        AnimationClip south,
        AnimationClip west,
        AnimationClip east,
        AnimationClip north)
    {
        ArgumentException.ThrowIfNullOrEmpty(name, nameof(name));
        ArgumentNullException.ThrowIfNull(south, nameof(south));
        ArgumentNullException.ThrowIfNull(west, nameof(west));
        ArgumentNullException.ThrowIfNull(east, nameof(east));
        ArgumentNullException.ThrowIfNull(north, nameof(north));

        DirectionalVariant[] variants =
        [
            new(south, SpriteEffects.None),
            new(east, SpriteEffects.None),
            new(east, SpriteEffects.None),
            new(north, SpriteEffects.None),
            new(north, SpriteEffects.None),
            new(north, SpriteEffects.None),
            new(west, SpriteEffects.None),
            new(south, SpriteEffects.None)
        ];

        return new DirectionalAnimationClip(name, variants);
    }

    /// <summary>
    /// Creates a single non-directional clip mapped to all 8 headings (e.g. rotating spell projectiles or stationary effects).
    /// </summary>
    /// <param name="name">The action name.</param>
    /// <param name="clip">The clip to use for all directions.</param>
    /// <returns>A new <see cref="DirectionalAnimationClip"/> instance.</returns>
    public static DirectionalAnimationClip CreateSingle(string name, AnimationClip clip)
    {
        ArgumentException.ThrowIfNullOrEmpty(name, nameof(name));
        ArgumentNullException.ThrowIfNull(clip, nameof(clip));

        DirectionalVariant[] variants = new DirectionalVariant[8];
        DirectionalVariant variant = new(clip, SpriteEffects.None);

        for (int i = 0; i < 8; i++)
        {
            variants[i] = variant;
        }

        return new DirectionalAnimationClip(name, variants);
    }

    /// <summary>
    /// Internal representation of a directional clip variation.
    /// </summary>
    private readonly record struct DirectionalVariant(AnimationClip Clip, SpriteEffects Effects);
}
