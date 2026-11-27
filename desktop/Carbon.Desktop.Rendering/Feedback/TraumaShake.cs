// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;

namespace Carbon.Desktop.Rendering.Feedback;

/// <summary>
/// Trauma-driven camera shake (Eiserloh, GDC 2016; docs/art/combat-animation.md §1.5, §3.4). Hits add trauma in
/// <c>[0, 1]</c>, which decays linearly; the offset is trauma² × <see cref="MaxOffset"/> along smooth noise, so small hits
/// barely move the camera and stacked hits build up. Presentation only: it offsets the world view, never the simulation.
/// </summary>
public struct TraumaShake
{
    /// <summary>Largest camera offset in world units at full trauma.</summary>
    public const float MaxOffset = 3f;

    /// <summary>Trauma lost per second.</summary>
    public const float DecayPerSecond = 2.5f;

    private float _time;

    /// <summary>Gets the current trauma in <c>[0, 1]</c>.</summary>
    public float Trauma { get; private set; }

    /// <summary>Adds <paramref name="amount"/> of trauma (clamped to 1).</summary>
    /// <param name="amount">Trauma to add.</param>
    public void Add(float amount) => this.Trauma = Math.Clamp(this.Trauma + MathF.Max(0f, amount), 0f, 1f);

    /// <summary>Advances the noise clock and decays trauma.</summary>
    /// <param name="dt">Frame seconds.</param>
    public void Update(float dt)
    {
        _time += dt;
        this.Trauma = MathF.Max(0f, this.Trauma - (DecayPerSecond * dt));
    }

    /// <summary>Gets the camera offset, or zero when <paramref name="enabled"/> is off.</summary>
    /// <param name="enabled">The screen-shake setting.</param>
    /// <returns>World-unit offset.</returns>
    public readonly Vector2 Offset(bool enabled)
    {
        if (!enabled || this.Trauma <= 0f)
        {
            return Vector2.Zero;
        }

        float amp = this.Trauma * this.Trauma * MaxOffset;
        return new Vector2(SmoothNoise(_time * 31f), SmoothNoise((_time * 27f) + 17.3f)) * amp;
    }

    /// <summary>Clears all trauma (setting turned off, world left).</summary>
    public void Reset() => this.Trauma = 0f;

    // Sum of incommensurate sines: smooth, zero-mean, in [-1, 1].
    private static float SmoothNoise(float x) => (MathF.Sin(x) * 0.6f) + (MathF.Sin((x * 1.73f) + 1.1f) * 0.4f);
}
