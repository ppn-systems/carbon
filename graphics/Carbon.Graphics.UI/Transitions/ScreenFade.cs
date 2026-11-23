// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.OpenGL.Abstractions;

namespace Carbon.Graphics.UI.Transitions;

/// <summary>
/// Controls full-screen color fade transitions (Fade In / Fade Out) for map loading, teleportation, and cutscenes.
/// Zero heap allocation on update and draw.
/// </summary>
public sealed class ScreenFade
{
    private float _duration = 1.0f;
    private float _elapsed;
    private bool _isActive;
    private bool _isFadeIn; // true: opaque -> transparent; false: transparent -> opaque

    /// <summary>
    /// Occurs when a fade transition completes.
    /// </summary>
    public event Action? FadeCompleted;

    /// <summary>
    /// Gets the current normalized alpha opacity [0.0 - 1.0].
    /// </summary>
    public float Alpha { get; private set; }

    /// <summary>
    /// Gets or sets the target fade color (typically black or white).
    /// </summary>
    public ColorRgba Color { get; set; } = ColorRgba.Black;

    /// <summary>
    /// Gets a value indicating whether a fade transition is currently actively running.
    /// </summary>
    public bool IsActive => _isActive;

    /// <summary>
    /// Starts a fade out transition (from transparent to opaque color).
    /// </summary>
    /// <param name="duration">Duration in seconds.</param>
    /// <param name="color">Optional color tint. Defaults to black.</param>
    public void FadeOut(float duration, ColorRgba? color = null)
    {
        _duration = Math.Max(0.001f, duration);
        _elapsed = 0f;
        _isFadeIn = false;
        _isActive = true;
        this.Alpha = 0f;
        if (color.HasValue)
        {
            this.Color = color.Value;
        }
    }

    /// <summary>
    /// Starts a fade in transition (from opaque color to transparent screen).
    /// </summary>
    /// <param name="duration">Duration in seconds.</param>
    /// <param name="color">Optional color tint. Defaults to black.</param>
    public void FadeIn(float duration, ColorRgba? color = null)
    {
        _duration = Math.Max(0.001f, duration);
        _elapsed = 0f;
        _isFadeIn = true;
        _isActive = true;
        this.Alpha = 1f;
        if (color.HasValue)
        {
            this.Color = color.Value;
        }
    }

    /// <summary>
    /// Advances the transition timeline by elapsed frame time in seconds.
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

        // Smooth cubic ease in/out
        float eased = progress * progress * (3f - (2f * progress));

        this.Alpha = _isFadeIn ? (1f - eased) : eased;

        if (progress >= 1f)
        {
            _isActive = false;
            this.Alpha = _isFadeIn ? 0f : 1f;
            this.FadeCompleted?.Invoke();
        }
    }

    /// <summary>
    /// Renders the fullscreen fade overlay quad using the provided 1x1 white pixel texture.
    /// </summary>
    /// <param name="spriteBatch">Target sprite batch.</param>
    /// <param name="screenBounds">Total screen viewport bounds.</param>
    /// <param name="whitePixel">1x1 white pixel texture.</param>
    /// <param name="layer">Rendering layer index (default 9999 on top of everything).</param>
    public void Draw(
        SpriteBatch spriteBatch,
        in RectangleF screenBounds,
        ITexture whitePixel,
        int layer = 9999)
    {
        ArgumentNullException.ThrowIfNull(spriteBatch, nameof(spriteBatch));
        ArgumentNullException.ThrowIfNull(whitePixel, nameof(whitePixel));

        if (this.Alpha <= 0.001f)
        {
            return;
        }

        byte a = (byte)(this.Alpha * 255f);
        ColorRgba fadeColor = new(this.Color.R, this.Color.G, this.Color.B, a);

        spriteBatch.Draw(
            whitePixel,
            new Vector2(screenBounds.X, screenBounds.Y),
            sourceRectangle: null,
            fadeColor,
            rotation: 0f,
            origin: Vector2.Zero,
            scale: new Vector2(screenBounds.Width, screenBounds.Height),
            effects: SpriteEffects.None,
            depth: 0f,
            footY: screenBounds.Bottom,
            layer: layer);
    }
}
