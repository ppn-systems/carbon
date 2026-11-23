// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Animation.Clips;
using Carbon.Graphics.Batching.Common;

namespace Carbon.Graphics.Animation.Controllers;

/// <summary>
/// Represents an individual visual layer in a multi-layered paperdoll animation hierarchy (e.g. Body, Armor, Weapon, Hair).
/// </summary>
public sealed class AnimationLayer
{
    /// <summary>
    /// Gets the unique identifier of this layer (e.g. "Body", "Armor", "Weapon", "Hair").
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets or sets the directional clip assigned to this layer.
    /// </summary>
    public DirectionalAnimationClip? DirectionalClip { get; set; }

    /// <summary>
    /// Gets or sets a standalone non-directional clip assigned to this layer.
    /// </summary>
    public AnimationClip? SingleClip { get; set; }

    /// <summary>
    /// Gets or sets the color tint applied when rendering this layer (e.g. for dye colors or skin tones).
    /// </summary>
    public ColorRgba Tint { get; set; } = ColorRgba.White;

    /// <summary>
    /// Gets or sets the sorting order offset relative to other layers. Lower values draw first (behind).
    /// </summary>
    public int ZOrder { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this layer is currently rendered.
    /// </summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>
    /// Gets or sets an additional positional offset for this specific layer.
    /// </summary>
    public Vector2 Offset { get; set; } = Vector2.Zero;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnimationLayer"/> class.
    /// </summary>
    /// <param name="name">The unique layer name.</param>
    /// <param name="zOrder">The drawing order priority.</param>
    /// <param name="tint">Optional tint color.</param>
    public AnimationLayer(string name, int zOrder = 0, ColorRgba? tint = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name, nameof(name));
        this.Name = name;
        this.ZOrder = zOrder;
        this.Tint = tint ?? ColorRgba.White;
    }
}
