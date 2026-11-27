// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Desktop.Abstractions.World;

namespace Carbon.Desktop.Rendering.Feedback;

/// <summary>
/// Client-side hit-stop for the local hero's swing (docs/art/combat-animation.md §1.4, §3.4). It never touches the session:
/// the action's clock, input and the simulation keep running. Only the displayed clip time is remapped. The pose holds at
/// the moment of impact for the freeze, then catches up through recovery, so the clip still ends exactly when the action
/// does.
/// </summary>
public struct HitStop
{
    /// <summary>Shortest freeze in seconds.</summary>
    public const float MinSeconds = 0.05f;

    /// <summary>Longest freeze in seconds.</summary>
    public const float MaxSeconds = 0.09f;

    private ActionAnimationKind _kind;
    private byte _combo;
    private float _duration;
    private float _lastProgress;
    private float _freezeAt;
    private float _freezeLength;

    /// <summary>Gets whether a freeze is attached to the current swing.</summary>
    public readonly bool IsArmed => _freezeLength > 0f;

    /// <summary>
    /// Gets the freeze length for a hit: heavier hits (damage relative to the target's max health) freeze longer, which
    /// also lengthens the stop on a critical hit, since it deals more damage.
    /// </summary>
    /// <param name="damage">Damage dealt.</param>
    /// <param name="targetMaxHealth">Target max health (0 when unknown).</param>
    /// <returns>Freeze seconds.</returns>
    public static float SecondsFor(float damage, float targetMaxHealth)
    {
        float share = targetMaxHealth > 0f ? Math.Clamp(damage / targetMaxHealth, 0f, 1f) : 0.1f;
        return MinSeconds + ((MaxSeconds - MinSeconds) * MathF.Min(1f, share * 2.5f));
    }

    /// <summary>
    /// Maps the action's real progress to the displayed clip time for a freeze of <paramref name="freezeLength"/>
    /// (normalized) starting at <paramref name="freezeAt"/>: identity before, held during, then a linear catch-up that
    /// reaches 1 at 1.
    /// </summary>
    /// <param name="progress">Real normalized progress.</param>
    /// <param name="freezeAt">Normalized time the freeze starts.</param>
    /// <param name="freezeLength">Normalized freeze length.</param>
    /// <returns>Displayed normalized progress.</returns>
    public static float Remap(float progress, float freezeAt, float freezeLength)
    {
        float p = Math.Clamp(progress, 0f, 1f);
        float z = Math.Clamp(freezeLength, 0f, MathF.Max(0f, (1f - freezeAt) * 0.8f));
        if (z <= 0f || p <= freezeAt)
        {
            return p;
        }

        if (p <= freezeAt + z)
        {
            return freezeAt;
        }

        return freezeAt + ((p - freezeAt - z) * (1f - freezeAt) / (1f - freezeAt - z));
    }

    /// <summary>
    /// Arms a freeze of <paramref name="seconds"/> on <paramref name="action"/>, starting at <paramref name="freezeAt"/> or
    /// at the current progress if that is later (a hit confirmed after contact presents on arrival).
    /// </summary>
    /// <param name="action">The local swing.</param>
    /// <param name="freezeAt">Normalized impact time.</param>
    /// <param name="seconds">Freeze seconds.</param>
    public void Arm(in ActionState action, float freezeAt, float seconds)
    {
        if (!action.IsActive || action.DurationSeconds <= 0f || (this.Matches(in action) && this.IsArmed))
        {
            return;
        }

        this.Track(in action);
        _freezeAt = MathF.Max(freezeAt, action.Progress);
        _freezeLength = seconds / action.DurationSeconds;
    }

    /// <summary>Gets the clip time to display for <paramref name="action"/> (its real progress when no freeze applies).</summary>
    /// <param name="action">The local action.</param>
    /// <returns>Displayed normalized progress.</returns>
    public float Displayed(in ActionState action)
    {
        if (!action.IsActive)
        {
            _freezeLength = 0f;
            return action.Progress;
        }

        if (!this.Matches(in action))
        {
            // A new swing started (or none was tracked): no freeze carries over.
            this.Track(in action);
            _freezeLength = 0f;
        }

        _lastProgress = action.Progress;
        return Remap(action.Progress, _freezeAt, _freezeLength);
    }

    private readonly bool Matches(in ActionState action) =>
        action.Kind == _kind && action.ComboStep == _combo && MathF.Abs(action.DurationSeconds - _duration) < 1e-5f &&
        action.Progress + 1e-4f >= _lastProgress;

    private void Track(in ActionState action)
    {
        _kind = action.Kind;
        _combo = action.ComboStep;
        _duration = action.DurationSeconds;
        _lastProgress = action.Progress;
    }
}
