// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Graphics.Animation.Atlas;
using Carbon.Graphics.Animation.Clips;
using Carbon.Graphics.Animation.Directions;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Sprites;

namespace Carbon.Graphics.Animation.Controllers;

/// <summary>
/// Manages and synchronizes multiple animation layers (paperdoll compositing) for 2.5D MMORPG entities.
/// All equipment layers (Body, Armor, Weapon, Helmet) share a single master timeline to guarantee zero desynchronization.
/// </summary>
public sealed class LayeredSpriteAnimator
{
    private readonly List<AnimationLayer> _layers = [];
    private readonly List<AnimationLayer> _sortedLayers = [];
    private bool _isSortDirty = true;

    /// <summary>
    /// Gets the master animator driving the playback timeline and directional heading.
    /// </summary>
    public SpriteAnimator MasterAnimator { get; } = new();

    /// <summary>
    /// Gets the current facing direction.
    /// </summary>
    public Direction8 Direction => this.MasterAnimator.Direction;

    /// <summary>
    /// Gets the read-only list of configured layers.
    /// </summary>
    public IReadOnlyList<AnimationLayer> Layers => _layers;

    /// <summary>
    /// Adds a new animation layer to this composite animator.
    /// </summary>
    /// <param name="layer">The layer to add.</param>
    public void AddLayer(AnimationLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer, nameof(layer));
        _layers.Add(layer);
        _isSortDirty = true;
    }

    /// <summary>
    /// Removes a layer by its unique name.
    /// </summary>
    /// <param name="name">The name of the layer to remove.</param>
    /// <returns><see langword="true"/> if removed; otherwise, <see langword="false"/>.</returns>
    public bool RemoveLayer(string name)
    {
        int index = _layers.FindIndex(l => string.Equals(l.Name, name, StringComparison.Ordinal));
        if (index >= 0)
        {
            _layers.RemoveAt(index);
            _isSortDirty = true;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Retrieves a layer by name.
    /// </summary>
    /// <param name="name">The layer name.</param>
    /// <returns>The found layer, or <see langword="null"/> if not found.</returns>
    public AnimationLayer? FindLayer(string name) =>
        _layers.Find(l => string.Equals(l.Name, name, StringComparison.Ordinal));

    /// <summary>
    /// Sets the visibility of a specified layer.
    /// </summary>
    /// <param name="name">The layer name.</param>
    /// <param name="visible">Whether the layer should be rendered.</param>
    public void SetLayerVisibility(string name, bool visible)
    {
        if (this.FindLayer(name) is { } layer)
        {
            layer.IsVisible = visible;
        }
    }

    /// <summary>
    /// Sets the color tint of a specified layer.
    /// </summary>
    /// <param name="name">The layer name.</param>
    /// <param name="tint">The tint color.</param>
    public void SetLayerTint(string name, ColorRgba tint)
    {
        if (this.FindLayer(name) is { } layer)
        {
            layer.Tint = tint;
        }
    }

    /// <summary>
    /// Sets the Z-order priority of a specified layer.
    /// </summary>
    /// <param name="name">The layer name.</param>
    /// <param name="zOrder">The new Z-order priority.</param>
    public void SetLayerZOrder(string name, int zOrder)
    {
        AnimationLayer? layer = this.FindLayer(name);
        if (layer is not null && layer.ZOrder != zOrder)
        {
            layer.ZOrder = zOrder;
            _isSortDirty = true;
        }
    }

    /// <summary>
    /// Sets the facing direction across all layers.
    /// </summary>
    /// <param name="direction">The new facing direction.</param>
    public void SetDirection(Direction8 direction) => this.MasterAnimator.SetDirection(direction);

    /// <summary>
    /// Updates the playback timeline across all synchronized layers.
    /// </summary>
    /// <param name="deltaTime">Elapsed time in seconds.</param>
    public void Update(float deltaTime) => this.MasterAnimator.Update(deltaTime);

    /// <summary>
    /// Renders all visible layers in sorted Z-order into the target <see cref="SpriteBatch"/>.
    /// Zero heap allocation on hot path when layers collection is stable.
    /// </summary>
    /// <param name="spriteBatch">The target sprite batch.</param>
    /// <param name="position">The entity position.</param>
    /// <param name="color">Master color tint multiplied with each layer's tint.</param>
    /// <param name="rotation">Rotation angle in radians.</param>
    /// <param name="scale">Scale vector.</param>
    /// <param name="depth">Base depth value [0..1].</param>
    /// <param name="footY">2.5D base feet Y coordinate for depth sorting.</param>
    /// <param name="layer">Target render layer index.</param>
    public void Draw(
        SpriteBatch spriteBatch,
        Vector2 position,
        ColorRgba? color = null,
        float rotation = 0f,
        Vector2? scale = null,
        float depth = 0f,
        float footY = 0f,
        int layer = 0)
    {
        ArgumentNullException.ThrowIfNull(spriteBatch, nameof(spriteBatch));

        if (_layers.Count == 0)
        {
            return;
        }

        if (_isSortDirty)
        {
            this.SortLayers();
        }

        Direction8 currentDir = this.MasterAnimator.Direction;
        float elapsed = this.MasterAnimator.ElapsedTime;
        ColorRgba baseColor = color ?? ColorRgba.White;
        Vector2 finalScale = scale ?? Vector2.One;
        float finalFootY = footY == 0f ? position.Y : footY;

        for (int i = 0; i < _sortedLayers.Count; i++)
        {
            AnimationLayer animLayer = _sortedLayers[i];
            if (!animLayer.IsVisible)
            {
                continue;
            }

            AnimationClip? clip = null;
            SpriteEffects effects = SpriteEffects.None;

            if (animLayer.DirectionalClip != null)
            {
                clip = animLayer.DirectionalClip.GetClip(currentDir, out effects);
            }
            else if (animLayer.SingleClip is not null)
            {
                clip = animLayer.SingleClip;
            }

            if (clip is null || clip.FrameCount == 0)
            {
                continue;
            }

            _ = clip.Evaluate(elapsed, out AnimationFrame frame);
            if (!frame.Region.IsValid)
            {
                continue;
            }

            AtlasRegion region = frame.Region;
            Vector2 layerPos = position + frame.Offset + animLayer.Offset;
            SpriteEffects activeEffects = effects ^ frame.Effects;
            ColorRgba layerColor = baseColor * animLayer.Tint;

            spriteBatch.Draw(
                region.Texture,
                layerPos,
                region.Bounds,
                layerColor,
                rotation,
                region.Origin,
                finalScale,
                activeEffects,
                depth,
                finalFootY,
                layer);
        }
    }

    private void SortLayers()
    {
        _sortedLayers.Clear();
        _sortedLayers.AddRange(_layers);
        _sortedLayers.Sort((a, b) => a.ZOrder.CompareTo(b.ZOrder));
        _isSortDirty = false;
    }
}
