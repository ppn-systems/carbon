// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Graphics.Animation.Atlas;

namespace Carbon.Graphics.Animation.Clips;

/// <summary>
/// Represents an immutable sequence of <see cref="AnimationFrame"/> items defining a complete animation action.
/// </summary>
public sealed class AnimationClip
{
    private readonly AnimationFrame[] _frames;
    private readonly float[] _frameEndTimes;

    /// <summary>
    /// Gets the unique name of this animation clip.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the playback mode for this clip (Once, Loop, or PingPong).
    /// </summary>
    public AnimationPlaybackMode PlaybackMode { get; }

    /// <summary>
    /// Gets the total duration of the animation clip in seconds.
    /// </summary>
    public float TotalDuration { get; }

    /// <summary>
    /// Gets the total number of frames in this clip.
    /// </summary>
    public int FrameCount => _frames.Length;

    /// <summary>
    /// Gets the read-only memory view of the animation frames.
    /// </summary>
    public ReadOnlyMemory<AnimationFrame> Frames => _frames;

    /// <summary>
    /// Gets the frame at the specified index.
    /// </summary>
    /// <param name="index">The zero-based frame index.</param>
    /// <returns>The <see cref="AnimationFrame"/> at the specified index.</returns>
    public AnimationFrame this[int index] => this.GetFrame(index);

    /// <summary>
    /// Initializes a new instance of the <see cref="AnimationClip"/> class.
    /// </summary>
    /// <param name="name">The name of the animation clip.</param>
    /// <param name="frames">The sequence of frames. Must not be empty.</param>
    /// <param name="playbackMode">The playback mode.</param>
    /// <exception cref="ArgumentException">Thrown if name or frames are null or empty.</exception>
    public AnimationClip(
        string name,
        IReadOnlyList<AnimationFrame> frames,
        AnimationPlaybackMode playbackMode = AnimationPlaybackMode.Loop)
    {
        ArgumentException.ThrowIfNullOrEmpty(name, nameof(name));
        ArgumentNullException.ThrowIfNull(frames, nameof(frames));

        if (frames.Count == 0)
        {
            throw new ArgumentException("Animation clip must contain at least one frame.", nameof(frames));
        }

        this.Name = name;
        this.PlaybackMode = playbackMode;

        _frames = new AnimationFrame[frames.Count];
        _frameEndTimes = new float[frames.Count];

        float accumulated = 0f;
        for (int i = 0; i < frames.Count; i++)
        {
            _frames[i] = frames[i];
            accumulated += frames[i].Duration;
            _frameEndTimes[i] = accumulated;
        }

        this.TotalDuration = accumulated;
    }

    /// <summary>
    /// Gets the frame at the specified zero-based index. Clamps index to valid range.
    /// </summary>
    /// <param name="index">The zero-based frame index.</param>
    /// <returns>The clamped <see cref="AnimationFrame"/>.</returns>
    public AnimationFrame GetFrame(int index)
    {
        int clamped = Math.Clamp(index, 0, _frames.Length - 1);
        return _frames[clamped];
    }

    /// <summary>
    /// Finds the frame index corresponding to an elapsed time within the clip duration.
    /// </summary>
    /// <param name="timeInSeconds">The elapsed time in seconds.</param>
    /// <returns>The zero-based frame index active at that time.</returns>
    public int GetFrameIndexAtTime(float timeInSeconds)
    {
        if (_frames.Length == 1 || timeInSeconds <= 0f)
        {
            return 0;
        }

        if (timeInSeconds >= this.TotalDuration)
        {
            return _frames.Length - 1;
        }

        // Binary search in sorted _frameEndTimes
        int low = 0;
        int high = _frames.Length - 1;

        while (low <= high)
        {
            int mid = low + ((high - low) / 2);
            float endTime = _frameEndTimes[mid];
            float startTime = mid == 0 ? 0f : _frameEndTimes[mid - 1];

            if (timeInSeconds >= startTime && timeInSeconds < endTime)
            {
                return mid;
            }

            if (timeInSeconds < startTime)
            {
                high = mid - 1;
            }
            else
            {
                low = mid + 1;
            }
        }

        return Math.Clamp(low, 0, _frames.Length - 1);
    }

    /// <summary>
    /// Evaluates the active frame and its index at the given elapsed time.
    /// </summary>
    /// <param name="timeInSeconds">The elapsed time in seconds.</param>
    /// <param name="frame">The active frame output.</param>
    /// <returns>The active frame index.</returns>
    public int Evaluate(float timeInSeconds, out AnimationFrame frame)
    {
        int index = this.GetFrameIndexAtTime(timeInSeconds);
        frame = _frames[index];
        return index;
    }

    /// <summary>
    /// Creates a uniform animation clip where every frame has an equal duration.
    /// </summary>
    /// <param name="name">The clip name.</param>
    /// <param name="regions">The sequence of atlas regions.</param>
    /// <param name="frameDuration">The duration for every frame in seconds.</param>
    /// <param name="playbackMode">The playback mode.</param>
    /// <returns>A new <see cref="AnimationClip"/> instance.</returns>
    public static AnimationClip CreateUniform(
        string name,
        IReadOnlyList<AtlasRegion> regions,
        float frameDuration,
        AnimationPlaybackMode playbackMode = AnimationPlaybackMode.Loop)
    {
        ArgumentNullException.ThrowIfNull(regions, nameof(regions));

        AnimationFrame[] frames = new AnimationFrame[regions.Count];
        for (int i = 0; i < regions.Count; i++)
        {
            frames[i] = new AnimationFrame(regions[i], frameDuration);
        }

        return new AnimationClip(name, frames, playbackMode);
    }

    /// <summary>
    /// Creates a uniform animation clip from a sprite atlas by region names.
    /// </summary>
    /// <param name="name">The clip name.</param>
    /// <param name="atlas">The sprite atlas containing the regions.</param>
    /// <param name="regionNames">The names of the regions in sequential order.</param>
    /// <param name="frameDuration">The duration for each frame in seconds.</param>
    /// <param name="playbackMode">The playback mode.</param>
    /// <returns>A new <see cref="AnimationClip"/> instance.</returns>
    public static AnimationClip CreateFromAtlas(
        string name,
        SpriteAtlas atlas,
        IReadOnlyList<string> regionNames,
        float frameDuration,
        AnimationPlaybackMode playbackMode = AnimationPlaybackMode.Loop)
    {
        ArgumentNullException.ThrowIfNull(atlas, nameof(atlas));
        ArgumentNullException.ThrowIfNull(regionNames, nameof(regionNames));

        AnimationFrame[] frames = new AnimationFrame[regionNames.Count];
        for (int i = 0; i < regionNames.Count; i++)
        {
            AtlasRegion region = atlas.GetRegion(regionNames[i]);
            frames[i] = new AnimationFrame(region, frameDuration);
        }

        return new AnimationClip(name, frames, playbackMode);
    }
}
