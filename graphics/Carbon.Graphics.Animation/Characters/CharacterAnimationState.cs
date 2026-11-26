// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;

namespace Carbon.Graphics.Animation.Characters;

/// <summary>
/// Per-entity character animation playback: the active clip, its elapsed time and the facing. Plain value type,
/// so a client can keep one per entity in a pooled array without allocations.
/// </summary>
public struct CharacterAnimationState
{
    /// <summary>Speed (world units per second) below which a character counts as standing.</summary>
    public const float MovingThreshold = 8f;

    /// <summary>Gets the active clip.</summary>
    public CharacterClip Clip { get; private set; }

    /// <summary>Gets the seconds elapsed in the active clip.</summary>
    public float Time { get; private set; }

    /// <summary>Gets whether the character faces left (screen -X).</summary>
    public bool FacingLeft { get; private set; }

    /// <summary>
    /// Switches to <paramref name="clip"/>, restarting it only when it changes or <paramref name="restart"/> is set.
    /// </summary>
    public void Play(CharacterClip clip, bool restart = false)
    {
        if (restart || clip != this.Clip)
        {
            this.Clip = clip;
            this.Time = 0f;
        }
    }

    /// <summary>
    /// Advances the active clip by <paramref name="deltaSeconds"/> scaled by <paramref name="rate"/>.
    /// </summary>
    public void Advance(float deltaSeconds, float rate = 1f)
    {
        if (deltaSeconds > 0f && rate > 0f)
        {
            this.Time += deltaSeconds * rate;
        }
    }

    /// <summary>
    /// Faces along the horizontal component of <paramref name="direction"/>; a (near) vertical or zero direction
    /// keeps the current facing, so walking straight up or down does not flicker the sprite.
    /// </summary>
    public void Face(Vector2 direction)
    {
        if (direction.X < -0.01f)
        {
            this.FacingLeft = true;
        }
        else if (direction.X > 0.01f)
        {
            this.FacingLeft = false;
        }
    }

    /// <summary>
    /// Locomotion helper: plays walk while <paramref name="velocity"/> exceeds <see cref="MovingThreshold"/> (idle
    /// otherwise), faces the movement and advances time. A one-shot clip (attack, hurt) is left to finish first,
    /// and death is never replaced.
    /// </summary>
    /// <param name="set">The archetype's clips (for one-shot durations).</param>
    /// <param name="velocity">Current velocity in world units per second.</param>
    /// <param name="deltaSeconds">Frame time.</param>
    /// <param name="walkRate">Walk playback rate (e.g. proportional to speed).</param>
    public void UpdateLocomotion(CharacterAnimationSet set, Vector2 velocity, float deltaSeconds, float walkRate = 1f)
    {
        System.ArgumentNullException.ThrowIfNull(set);

        bool moving = velocity.LengthSquared() > MovingThreshold * MovingThreshold;
        bool locomotion = this.Clip is CharacterClip.Idle or CharacterClip.Walk;
        bool busy = this.Clip == CharacterClip.Death || (!locomotion && this.Time < set.GetClip(this.Clip).TotalDuration);
        if (!busy)
        {
            this.Play(moving ? CharacterClip.Walk : CharacterClip.Idle);
        }

        if (moving && this.Clip != CharacterClip.Death)
        {
            this.Face(velocity);
        }

        this.Advance(deltaSeconds, this.Clip == CharacterClip.Walk ? walkRate : 1f);
    }
}
