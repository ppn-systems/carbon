// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.OpenGL.Abstractions;

namespace Carbon.Graphics.Scenes.Transitions;

/// <summary>
/// Orchestrates two-phase fullscreen transition effects (Fade-Out -> Swap -> Fade-In) during scene switches.
/// Zero heap allocation during execution.
/// </summary>
public sealed class SceneTransition
{
    private enum TransitionPhase
    {
        Idle = 0,
        FadeOut = 1,
        FadeIn = 2
    }

    private TransitionPhase _phase = TransitionPhase.Idle;
    private SceneTransitionEffect _effect = SceneTransitionEffect.None;
    private float _halfDuration;
    private float _elapsed;
    private ColorRgba _color = ColorRgba.Black;
    private Action? _onMidpointSwap;

    /// <summary>
    /// Gets a value indicating whether a transition is actively running.
    /// </summary>
    public bool IsActive => _phase != TransitionPhase.Idle;

    /// <summary>
    /// Gets the visual transition effect style currently active.
    /// </summary>
    public SceneTransitionEffect Effect => _effect;

    /// <summary>
    /// Gets the current normalized alpha opacity [0.0 - 1.0].
    /// </summary>
    public float Alpha { get; private set; }

    /// <summary>
    /// Gets the current tint color for this transition overlay.
    /// </summary>
    public ColorRgba Color => _color;

    /// <summary>
    /// Starts a two-phase transition sequence.
    /// </summary>
    /// <param name="effect">Visual transition effect.</param>
    /// <param name="duration">Total transition duration in seconds (split equally between FadeOut and FadeIn).</param>
    /// <param name="color">The overlay color tint (e.g. black for fade, white for flash).</param>
    /// <param name="onMidpointSwap">Callback executed when the screen is fully obscured to swap the active scenes.</param>
    public void Start(SceneTransitionEffect effect, float duration, ColorRgba color, Action onMidpointSwap)
    {
        ArgumentNullException.ThrowIfNull(onMidpointSwap, nameof(onMidpointSwap));

        if (effect == SceneTransitionEffect.None || duration <= 0f)
        {
            _phase = TransitionPhase.Idle;
            this.Alpha = 0f;
            onMidpointSwap();
            return;
        }

        _effect = effect;
        _halfDuration = Math.Max(0.001f, duration * 0.5f);
        _elapsed = 0f;
        _color = color;
        _onMidpointSwap = onMidpointSwap;
        _phase = TransitionPhase.FadeOut;
        this.Alpha = 0f;
    }

    /// <summary>
    /// Advances transition timeline simulation each tick.
    /// Zero heap allocation.
    /// </summary>
    /// <param name="dt">Elapsed delta time in seconds.</param>
    public void Update(double dt)
    {
        if (_phase == TransitionPhase.Idle)
        {
            return;
        }

        _elapsed += (float)dt;
        float progress = Math.Clamp(_elapsed / _halfDuration, 0f, 1f);

        // Smooth cubic ease
        float eased = progress * progress * (3f - (2f * progress));

        if (_phase == TransitionPhase.FadeOut)
        {
            this.Alpha = eased;
            if (progress >= 1f)
            {
                this.Alpha = 1f;
                _phase = TransitionPhase.FadeIn;
                _elapsed = 0f;
                _onMidpointSwap?.Invoke();
                _onMidpointSwap = null;
            }
        }
        else if (_phase == TransitionPhase.FadeIn)
        {
            this.Alpha = 1f - eased;
            if (progress >= 1f)
            {
                this.Alpha = 0f;
                _phase = TransitionPhase.Idle;
            }
        }
    }

    /// <summary>
    /// Renders the fullscreen transition overlay quad if active.
    /// </summary>
    /// <param name="spriteBatch">Target sprite batch renderer.</param>
    /// <param name="whitePixel">1x1 white pixel texture.</param>
    /// <param name="screenBounds">Total screen bounds rectangle.</param>
    public void Render(SpriteBatch spriteBatch, ITexture whitePixel, in RectangleF screenBounds)
    {
        ArgumentNullException.ThrowIfNull(spriteBatch, nameof(spriteBatch));
        ArgumentNullException.ThrowIfNull(whitePixel, nameof(whitePixel));

        if (!this.IsActive || this.Alpha <= 0.001f)
        {
            return;
        }

        byte a = (byte)Math.Clamp((int)(this.Alpha * _color.A), 0, 255);
        ColorRgba drawColor = new(_color.R, _color.G, _color.B, a);

        spriteBatch.Draw(whitePixel, screenBounds, drawColor);
    }
}
