// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Desktop.Rendering.Feedback;

/// <summary>Player settings for impact feedback (<c>[display] screen_shake</c>, <c>reduce_flashes</c>; docs/art/combat-animation.md §3.4).</summary>
/// <param name="ScreenShake">Whether hits shake the camera. Off means no camera offset at all.</param>
/// <param name="ReduceFlashes">Whether hit flashes are softened to a gentle tint.</param>
public readonly record struct ImpactFeelOptions(bool ScreenShake = true, bool ReduceFlashes = false)
{
    /// <summary>Strength of a hit flash under <see cref="ReduceFlashes"/>.</summary>
    public const float ReducedFlashScale = 0.35f;

    /// <summary>Gets the defaults: shake on, full flashes.</summary>
    public static ImpactFeelOptions Default => new(true, false);

    /// <summary>Scales a hit flash strength by the flash setting.</summary>
    /// <param name="flash">Flash strength 0..1.</param>
    /// <returns>The flash to draw.</returns>
    public float ScaleFlash(float flash) => this.ReduceFlashes ? flash * ReducedFlashScale : flash;
}
