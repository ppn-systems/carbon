// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Sprites;

namespace Carbon.Graphics.Scenes.Abstractions;

/// <summary>
/// Defines the contract for visual objects residing in a scene with transform, layering, simulation, and batch rendering.
/// </summary>
public interface IRenderObject
{
    /// <summary>
    /// Gets or sets the world position coordinates of this object.
    /// </summary>
    Vector2 Position { get; set; }

    /// <summary>
    /// Gets or sets the scale multipliers along horizontal and vertical axes.
    /// </summary>
    Vector2 Scale { get; set; }

    /// <summary>
    /// Gets or sets the rotation angle in radians.
    /// </summary>
    float Rotation { get; set; }

    /// <summary>
    /// Gets or sets the local pivot point around which rotation and scaling occur.
    /// </summary>
    Vector2 Origin { get; set; }

    /// <summary>
    /// Gets or sets the color tint applied when rendering.
    /// </summary>
    ColorRgba Color { get; set; }

    /// <summary>
    /// Gets or sets the render layer index for multi-pass visual sorting.
    /// </summary>
    int Layer { get; set; }

    /// <summary>
    /// Gets or sets the explicit Z-Index used for secondary depth sorting within the same layer.
    /// </summary>
    int ZIndex { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this object should be submitted for rendering.
    /// </summary>
    bool IsVisible { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this object receives simulation tick updates.
    /// </summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// Gets or sets an optional tag identifier for classification and query lookup.
    /// </summary>
    string? Tag { get; set; }

    /// <summary>
    /// Gets the axis-aligned world bounding box for camera frustum culling.
    /// </summary>
    RectangleF Bounds { get; }

    /// <summary>
    /// Advances object simulation logic each tick.
    /// Zero heap allocation.
    /// </summary>
    /// <param name="dt">Elapsed delta time in seconds.</param>
    void Update(double dt);

    /// <summary>
    /// Submits geometry and texture commands into the active batch renderer.
    /// Zero heap allocation.
    /// </summary>
    /// <param name="spriteBatch">The active sprite batch renderer.</param>
    void Draw(SpriteBatch spriteBatch);
}
