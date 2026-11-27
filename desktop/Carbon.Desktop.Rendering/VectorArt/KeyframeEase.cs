// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Desktop.Rendering.VectorArt;

/// <summary>
/// Easing of the segment that leaves a keyframe (<c>"ease"</c> on a <c>.cvg.json</c> track; default
/// <see cref="InOut"/>, the original smoothstep). Attack clips leave the cocked pose with <see cref="Out"/> so the strike
/// starts at full speed (<c>docs/art/combat-animation.md</c> §3.2).
/// </summary>
public enum KeyframeEase : byte
{
    /// <summary>Smoothstep: slow out, slow in.</summary>
    InOut = 0,

    /// <summary>Constant speed.</summary>
    Linear = 1,

    /// <summary>Cubic ease-in: starts slow, arrives fast.</summary>
    In = 2,

    /// <summary>Cubic ease-out: leaves fast, settles into the next key.</summary>
    Out = 3,
}

/// <summary>Evaluates <see cref="KeyframeEase"/> curves.</summary>
public static class KeyframeEasing
{
    /// <summary>Maps linear segment progress <paramref name="t"/> (0..1) through <paramref name="ease"/>.</summary>
    /// <param name="ease">Easing.</param>
    /// <param name="t">Linear progress.</param>
    /// <returns>Eased progress (0..1).</returns>
    public static float Apply(KeyframeEase ease, float t)
    {
        float k = Math.Clamp(t, 0f, 1f);
        return ease switch
        {
            KeyframeEase.Linear => k,
            KeyframeEase.In => k * k * k,
            KeyframeEase.Out => 1f - ((1f - k) * (1f - k) * (1f - k)),
            KeyframeEase.InOut or _ => k * k * (3f - (2f * k)),
        };
    }

    /// <summary>Parses a <c>.cvg.json</c> ease name (case-insensitive; unknown or missing is <see cref="KeyframeEase.InOut"/>).</summary>
    /// <param name="name">Ease name.</param>
    /// <returns>The ease.</returns>
    public static KeyframeEase Parse(string? name) => name?.ToUpperInvariant() switch
    {
        "LINEAR" => KeyframeEase.Linear,
        "IN" => KeyframeEase.In,
        "OUT" => KeyframeEase.Out,
        _ => KeyframeEase.InOut,
    };
}
