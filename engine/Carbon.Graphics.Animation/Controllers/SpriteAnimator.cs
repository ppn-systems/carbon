// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Animation.Atlas;
using Carbon.Graphics.Animation.Clips;
using Carbon.Graphics.Animation.Directions;
using Carbon.Graphics.Animation.Events;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Sprites;

namespace Carbon.Graphics.Animation.Controllers;

/// <summary>
/// Controls playback, state transitions, direction handling, and event dispatching for 2D/2.5D sprite animations.
/// Designed for zero heap allocation during runtime tick updates and draw calls.
/// </summary>
public sealed class SpriteAnimator
{
    private AnimationClip? _currentClip;
    private DirectionalAnimationClip? _currentDirectionalClip;
    private Direction8 _direction = Direction8.South;
    private SpriteEffects _directionalEffects = SpriteEffects.None;
    private AnimationPlaybackMode _playbackMode = AnimationPlaybackMode.Loop;

    private int _currentFrameIndex;
    private AnimationFrame _currentFrame;
    private float _elapsedTime;
    private float _speed = 1.0f;
    private bool _isPlaying;
    private bool _isFinished;
    private bool _pingPongForward = true;

    #region Events

    /// <summary>
    /// Occurs when the active frame index changes during playback.
    /// </summary>
    public event FrameChangedCallback? FrameChanged;

    /// <summary>
    /// Occurs when a non-looping animation reaches its end and stops.
    /// </summary>
    public event AnimationCompletedCallback? Completed;

    /// <summary>
    /// Occurs when a looping or ping-pong animation completes a cycle.
    /// </summary>
    public event AnimationLoopedCallback? Looped;

    /// <summary>
    /// Occurs when an animation frame with an event tag is activated.
    /// </summary>
    public event AnimationCustomEventCallback? EventTriggered;

    #endregion Events

    #region Properties

    /// <summary>
    /// Gets the currently playing <see cref="AnimationClip"/>, if any.
    /// </summary>
    public AnimationClip? CurrentClip => _currentClip;

    /// <summary>
    /// Gets the currently active <see cref="DirectionalAnimationClip"/>, if playback was initiated from a directional clip.
    /// </summary>
    public DirectionalAnimationClip? CurrentDirectionalClip => _currentDirectionalClip;

    /// <summary>
    /// Gets the current 8-way facing direction.
    /// </summary>
    public Direction8 Direction => _direction;

    /// <summary>
    /// Gets the active playback mode (Once, Loop, or PingPong).
    /// </summary>
    public AnimationPlaybackMode PlaybackMode
    {
        get => _playbackMode;
        set => _playbackMode = value;
    }

    /// <summary>
    /// Gets the zero-based index of the currently active frame.
    /// </summary>
    public int CurrentFrameIndex => _currentFrameIndex;

    /// <summary>
    /// Gets the currently active <see cref="AnimationFrame"/>.
    /// </summary>
    public AnimationFrame CurrentFrame => _currentFrame;

    /// <summary>
    /// Gets the elapsed playback time in seconds within the current cycle.
    /// </summary>
    public float ElapsedTime => _elapsedTime;

    /// <summary>
    /// Gets the total duration of the currently playing clip in seconds.
    /// </summary>
    public float TotalDuration => _currentClip?.TotalDuration ?? 0f;

    /// <summary>
    /// Gets the normalized playback progress from 0.0 to 1.0.
    /// </summary>
    public float NormalizedProgress
    {
        get
        {
            float duration = this.TotalDuration;
            return duration > 0f ? Math.Clamp(_elapsedTime / duration, 0f, 1f) : 0f;
        }
    }

    /// <summary>
    /// Gets or sets the playback speed multiplier. Must be non-negative.
    /// </summary>
    public float Speed
    {
        get => _speed;
        set => _speed = Math.Max(0f, value);
    }

    /// <summary>
    /// Gets a value indicating whether the animation is currently playing.
    /// </summary>
    public bool IsPlaying => _isPlaying;

    /// <summary>
    /// Gets a value indicating whether the animation is currently paused.
    /// </summary>
    public bool IsPaused => !_isPlaying && !_isFinished && _currentClip != null;

    /// <summary>
    /// Gets a value indicating whether the animation has completed playback.
    /// </summary>
    public bool IsFinished => _isFinished;

    /// <summary>
    /// Gets the combined visual flip transformation for the current frame, taking into account directional mirroring.
    /// </summary>
    public SpriteEffects ActiveEffects => _directionalEffects ^ _currentFrame.Effects;

    #endregion Properties

    #region Playback Control

    /// <summary>
    /// Plays the specified non-directional animation clip.
    /// </summary>
    /// <param name="clip">The clip to play.</param>
    /// <param name="restart">Whether to restart playback from the beginning if already playing this clip.</param>
    public void Play(AnimationClip clip, bool restart = false)
    {
        ArgumentNullException.ThrowIfNull(clip, nameof(clip));

        if (!restart && ReferenceEquals(_currentClip, clip) && _isPlaying)
        {
            return;
        }

        _currentDirectionalClip = null;
        _directionalEffects = SpriteEffects.None;
        _currentClip = clip;
        _playbackMode = clip.PlaybackMode;

        this.ResetPlayback();
    }

    /// <summary>
    /// Plays a directional animation clip facing the specified direction.
    /// </summary>
    /// <param name="directionalClip">The directional action clip to play.</param>
    /// <param name="direction">The facing direction.</param>
    /// <param name="restart">Whether to restart playback from the beginning if already playing.</param>
    public void Play(DirectionalAnimationClip directionalClip, Direction8 direction, bool restart = false)
    {
        ArgumentNullException.ThrowIfNull(directionalClip, nameof(directionalClip));

        _direction = direction;
        _currentDirectionalClip = directionalClip;

        AnimationClip clip = directionalClip.GetClip(direction, out SpriteEffects flipEffects);
        _directionalEffects = flipEffects;

        if (!restart && ReferenceEquals(_currentClip, clip) && _isPlaying)
        {
            return;
        }

        _currentClip = clip;
        _playbackMode = clip.PlaybackMode;

        this.ResetPlayback();
    }

    /// <summary>
    /// Plays a directional animation clip using the current facing direction.
    /// </summary>
    /// <param name="directionalClip">The directional action clip to play.</param>
    /// <param name="restart">Whether to restart playback from the beginning if already playing.</param>
    public void Play(DirectionalAnimationClip directionalClip, bool restart = false) =>
        this.Play(directionalClip, _direction, restart);

    /// <summary>
    /// Updates the facing direction. If currently playing a directional clip, seamlessly switches the visual clip
    /// while preserving relative animation progress.
    /// </summary>
    /// <param name="direction">The new 8-way facing direction.</param>
    public void SetDirection(Direction8 direction)
    {
        if (_direction == direction)
        {
            return;
        }

        _direction = direction;

        if (_currentDirectionalClip != null)
        {
            AnimationClip nextClip = _currentDirectionalClip.GetClip(direction, out SpriteEffects flipEffects);
            _directionalEffects = flipEffects;

            if (!ReferenceEquals(_currentClip, nextClip))
            {
                _currentClip = nextClip;
                this.ApplyFrameAtCurrentTime(triggerEvents: false);
            }
        }
    }

    /// <summary>
    /// Pauses animation playback at the current frame.
    /// </summary>
    public void Pause()
    {
        if (_isPlaying)
        {
            _isPlaying = false;
        }
    }

    /// <summary>
    /// Resumes playback if currently paused.
    /// </summary>
    public void Resume()
    {
        if (!_isPlaying && !_isFinished && _currentClip != null)
        {
            _isPlaying = true;
        }
    }

    /// <summary>
    /// Stops animation playback and resets to the initial frame.
    /// </summary>
    public void Stop()
    {
        _isPlaying = false;
        _isFinished = false;
        this.ResetPlayback();
    }

    /// <summary>
    /// Resets playback time to the beginning and re-evaluates the first frame.
    /// </summary>
    public void Reset() => this.ResetPlayback();

    /// <summary>
    /// Jumps directly to the specified frame index.
    /// </summary>
    /// <param name="frameIndex">The target zero-based frame index.</param>
    public void SetFrame(int frameIndex)
    {
        if (_currentClip == null || _currentClip.FrameCount == 0)
        {
            return;
        }

        int clamped = Math.Clamp(frameIndex, 0, _currentClip.FrameCount - 1);
        int prev = _currentFrameIndex;
        _currentFrameIndex = clamped;
        _currentFrame = _currentClip.GetFrame(clamped);

        if (_currentFrameIndex != prev)
        {
            this.FrameChanged?.Invoke(this, _currentFrameIndex);

            if (!string.IsNullOrEmpty(_currentFrame.EventTag))
            {
                this.EventTriggered?.Invoke(this, _currentFrame.EventTag);
            }
        }
    }

    #endregion Playback Control

    #region Simulation & Rendering

    /// <summary>
    /// Advances the animation timeline by the specified delta time in seconds.
    /// Zero heap allocation on hot path.
    /// </summary>
    /// <param name="deltaTime">Elapsed frame time in seconds.</param>
    public void Update(float deltaTime)
    {
        if (!_isPlaying || _currentClip == null || _currentClip.FrameCount == 0)
        {
            return;
        }

        float delta = deltaTime * _speed;
        if (delta <= 0f)
        {
            return;
        }

        float duration = _currentClip.TotalDuration;
        if (duration <= 0f)
        {
            return;
        }

        switch (_playbackMode)
        {
            case AnimationPlaybackMode.Once:
                _elapsedTime += delta;
                if (_elapsedTime >= duration)
                {
                    _elapsedTime = duration;
                    _isPlaying = false;
                    _isFinished = true;
                    this.ApplyFrameAtCurrentTime(triggerEvents: true);
                    this.Completed?.Invoke(this);
                    return;
                }
                break;

            case AnimationPlaybackMode.Loop:
                _elapsedTime += delta;
                if (_elapsedTime >= duration)
                {
                    while (_elapsedTime >= duration)
                    {
                        _elapsedTime -= duration;
                    }

                    this.Looped?.Invoke(this);
                }
                break;

            case AnimationPlaybackMode.PingPong:
                if (_pingPongForward)
                {
                    _elapsedTime += delta;
                    if (_elapsedTime >= duration)
                    {
                        float overshoot = _elapsedTime - duration;
                        _elapsedTime = MathF.Max(0f, duration - overshoot);
                        _pingPongForward = false;
                        this.Looped?.Invoke(this);
                    }
                }
                else
                {
                    _elapsedTime -= delta;
                    if (_elapsedTime <= 0f)
                    {
                        float undershoot = -_elapsedTime;
                        _elapsedTime = MathF.Min(duration, undershoot);
                        _pingPongForward = true;
                        this.Looped?.Invoke(this);
                    }
                }
                break;
            default:
                break;
        }

        this.ApplyFrameAtCurrentTime(triggerEvents: true);
    }

    /// <summary>
    /// Renders the currently active animation frame using the provided <see cref="SpriteBatch"/>.
    /// Zero heap allocation.
    /// </summary>
    /// <param name="spriteBatch">The target sprite batch.</param>
    /// <param name="position">World or screen position of the sprite entity.</param>
    /// <param name="color">Optional color tint. Defaults to white.</param>
    /// <param name="rotation">Rotation angle in radians.</param>
    /// <param name="scale">Optional scale vector. Defaults to (1, 1).</param>
    /// <param name="depth">Depth buffer value [0..1].</param>
    /// <param name="footY">2.5D base feet Y coordinate for depth sorting. If 0, uses position.Y.</param>
    /// <param name="layer">Rendering layer index (higher values draw on top).</param>
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

        if (_currentClip == null || !_currentFrame.Region.IsValid)
        {
            return;
        }

        AtlasRegion region = _currentFrame.Region;
        Vector2 finalPos = position + _currentFrame.Offset;
        Vector2 finalScale = scale ?? Vector2.One;
        Vector2 finalOrigin = region.Origin;
        ColorRgba finalColor = color ?? ColorRgba.White;
        float finalFootY = footY == 0f ? finalPos.Y : footY;

        spriteBatch.Draw(
            region.Texture,
            finalPos,
            region.Bounds,
            finalColor,
            rotation,
            finalOrigin,
            finalScale,
            this.ActiveEffects,
            depth,
            finalFootY,
            layer);
    }

    #endregion Simulation & Rendering

    #region Private Helpers

    private void ResetPlayback()
    {
        _elapsedTime = 0f;
        _isFinished = false;
        _pingPongForward = true;
        _isPlaying = true;

        if (_currentClip != null && _currentClip.FrameCount > 0)
        {
            _currentFrameIndex = 0;
            _currentFrame = _currentClip.GetFrame(0);
            this.FrameChanged?.Invoke(this, 0);

            if (!string.IsNullOrEmpty(_currentFrame.EventTag))
            {
                this.EventTriggered?.Invoke(this, _currentFrame.EventTag);
            }
        }
    }

    private void ApplyFrameAtCurrentTime(bool triggerEvents)
    {
        if (_currentClip == null || _currentClip.FrameCount == 0)
        {
            return;
        }

        int nextIndex = _currentClip.GetFrameIndexAtTime(_elapsedTime);
        if (nextIndex != _currentFrameIndex)
        {
            _currentFrameIndex = nextIndex;
            _currentFrame = _currentClip.GetFrame(nextIndex);

            if (triggerEvents)
            {
                this.FrameChanged?.Invoke(this, _currentFrameIndex);

                if (!string.IsNullOrEmpty(_currentFrame.EventTag))
                {
                    this.EventTriggered?.Invoke(this, _currentFrame.EventTag);
                }
            }
        }
    }

    #endregion Private Helpers
}
