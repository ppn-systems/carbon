// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.OpenGL.Abstractions;

namespace Carbon.Graphics.UI.Transitions;

/// <summary>
/// Provides screen flash and damage vignette overlays (e.g. red flash on critical hit, white flash on level up).
/// Decays exponentially over a short duration.
/// </summary>
public sealed class ScreenFlash
{
    private float _duration = 0.3f;
    private float _elapsed;
    private float _peakAlpha = 0.8f;
    private bool _isActive;

    /// <summary>
    /// Gets the current opacity value [0.0 - 1.0].
    /// </summary>
    public float Alpha { get; private set; }

    /// <summary>
    /// Gets the active flash color tint.
    /// </summary>
    public ColorRgba Color { get; private set; } = new(255, 0, 0, 255);

    /// <summary>
    /// Gets a value indicating whether a flash effect is currently active.
    /// </summary>
    public bool IsActive => _isActive;

    /// <summary>
    /// Triggers a screen flash effect with specified color, duration, and maximum alpha.
    /// </summary>
    /// <param name="color">Flash color (e.g. Red for damage, Yellow for critical, White for holy).</param>
    /// <param name="duration">Duration in seconds.</param>
    /// <param name="peakAlpha">Maximum alpha opacity at peak.</param>
    public void Flash(ColorRgba color, float duration = 0.3f, float peakAlpha = 0.8f)
    {
        this.Color = color;
        _duration = Math.Max(0.001f, duration);
        _peakAlpha = Math.Clamp(peakAlpha, 0f, 1f);
        _elapsed = 0f;
        _isActive = true;
        this.Alpha = _peakAlpha;
    }

    /// <summary>
    /// Updates flash decay by elapsed delta time in seconds.
    /// </summary>
    /// <param name="deltaTime">Elapsed time in seconds.</param>
    public void Update(float deltaTime)
    {
        if (!_isActive)
        {
            return;
        }

        _elapsed += deltaTime;
        float progress = Math.Clamp(_elapsed / _duration, 0f, 1f);

        // Exponential decay
        float decay = 1f - progress;
        this.Alpha = _peakAlpha * decay * decay;

        if (progress >= 1f)
        {
            _isActive = false;
            this.Alpha = 0f;
        }
    }

    /// <summary>
    /// Renders the fullscreen flash overlay quad using the provided 1x1 white pixel texture.
    /// </summary>
    /// <param name="spriteBatch">Target sprite batch.</param>
    /// <param name="screenBounds">Total screen viewport bounds.</param>
    /// <param name="whitePixel">1x1 white pixel texture.</param>
    /// <param name="layer">Rendering layer index (default 9998).</param>
    public void Draw(
        SpriteBatch spriteBatch,
        in RectangleF screenBounds,
        ITexture whitePixel,
        int layer = 9998)
    {
        ArgumentNullException.ThrowIfNull(spriteBatch, nameof(spriteBatch));
        ArgumentNullException.ThrowIfNull(whitePixel, nameof(whitePixel));

        if (this.Alpha <= 0.001f)
        {
            return;
        }

        byte a = (byte)(this.Alpha * 255f);
        ColorRgba flashCol = new(this.Color.R, this.Color.G, this.Color.B, a);

        spriteBatch.Draw(
            whitePixel,
            new Vector2(screenBounds.X, screenBounds.Y),
            sourceRectangle: null,
            flashCol,
            rotation: 0f,
            origin: Vector2.Zero,
            scale: new Vector2(screenBounds.Width, screenBounds.Height),
            effects: SpriteEffects.None,
            depth: 0f,
            footY: screenBounds.Bottom,
            layer: layer);
    }
}
