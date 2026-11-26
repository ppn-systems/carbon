// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Animation.Clips;
using Carbon.Graphics.Batching.Common;

namespace Carbon.Graphics.Animation.Characters;

/// <summary>
/// The loaded clips and frame geometry of one visual archetype. Immutable after load; shared by every entity
/// that wears the archetype (per-entity playback lives in <see cref="CharacterAnimationState"/>).
/// </summary>
public sealed class CharacterAnimationSet
{
    private readonly AnimationClip?[] _clips;

    internal CharacterAnimationSet(CharacterArchetypeDefinition definition, AnimationClip?[] clips)
    {
        this.Key = definition.Key;
        this.FrameSize = new Vector2(definition.FrameWidth, definition.FrameHeight);
        this.Pivot = new Vector2(definition.PivotX, definition.PivotY);
        this.BodyHeight = definition.BodyHeight > 0f ? definition.BodyHeight : definition.FrameHeight;
        this.HeightTiles = definition.HeightTiles > 0f ? definition.HeightTiles : 1f;
        this.FacesLeft = definition.FacesLeft;
        _clips = clips;
    }

    /// <summary>Gets the archetype key.</summary>
    public string Key { get; }

    /// <summary>Gets the frame cell size in sheet pixels.</summary>
    public Vector2 FrameSize { get; }

    /// <summary>Gets the ground contact point inside a frame, in sheet pixels (also every frame region's origin).</summary>
    public Vector2 Pivot { get; }

    /// <summary>Gets the drawn body height (feet to head) in sheet pixels.</summary>
    public float BodyHeight { get; }

    /// <summary>Gets the on-screen body height in tiles.</summary>
    public float HeightTiles { get; }

    /// <summary>Gets whether the sheets face left.</summary>
    public bool FacesLeft { get; }

    /// <summary>
    /// Gets the sheet-pixel to world-unit scale that makes the body <see cref="HeightTiles"/> tiles tall.
    /// </summary>
    public float GetScale(float tileSize) => this.HeightTiles * tileSize / this.BodyHeight;

    /// <summary>
    /// Gets whether the archetype has its own sheet for <paramref name="clip"/> (without falling back).
    /// </summary>
    public bool Has(CharacterClip clip) => (uint)clip < (uint)_clips.Length && _clips[(int)clip] is not null;

    /// <summary>
    /// Gets the clip to play for <paramref name="clip"/>, following <see cref="CharacterClips.Fallback"/> when the
    /// archetype lacks it. Never null: every loaded set has an idle clip.
    /// </summary>
    public AnimationClip GetClip(CharacterClip clip)
    {
        CharacterClip current = clip;
        for (int guard = 0; guard < CharacterClips.Count; guard++)
        {
            if (this.Has(current))
            {
                return _clips[(int)current]!;
            }

            CharacterClip next = current.Fallback();
            if (next == current)
            {
                break;
            }

            current = next;
        }

        return _clips[(int)CharacterClip.Idle]!;
    }

    /// <summary>
    /// Gets the frame for <paramref name="state"/>: looping clips wrap, one-shot clips hold their last frame.
    /// </summary>
    public AnimationFrame GetFrame(in CharacterAnimationState state)
    {
        AnimationClip clip = this.GetClip(state.Clip);
        float time = state.Time;
        if (clip.PlaybackMode == AnimationPlaybackMode.Loop && clip.TotalDuration > 0f)
        {
            time %= clip.TotalDuration;
        }

        _ = clip.Evaluate(time, out AnimationFrame frame);
        return frame;
    }

    /// <summary>
    /// Gets the frame of <paramref name="clip"/> at a normalized progress in [0, 1], for clips driven by a
    /// gameplay phase timeline (e.g. an attack's windup/active/recovery) instead of wall-clock time.
    /// </summary>
    public AnimationFrame GetFrameAtProgress(CharacterClip clip, float progress)
    {
        AnimationClip resolved = this.GetClip(clip);
        int index = (int)(Math.Clamp(progress, 0f, 1f) * resolved.FrameCount);
        return resolved.GetFrame(Math.Min(index, resolved.FrameCount - 1));
    }

    /// <summary>
    /// Gets the mirror effect that makes the sprite face the direction stored in <paramref name="state"/>.
    /// </summary>
    public SpriteEffects GetEffects(in CharacterAnimationState state) =>
        state.FacingLeft != this.FacesLeft ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
}
