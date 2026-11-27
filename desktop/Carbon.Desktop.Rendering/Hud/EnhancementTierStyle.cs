// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Graphics.Batching.Common;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// How a slot holding an enhanced item looks (one row of <see cref="EnhancementTierStyles"/>). Rarity stays the slot
/// border; enhancement is the background <see cref="Fill"/>, the corner <c>+N</c> badge in <see cref="Accent"/>, and for
/// high tiers a glow (<see cref="Glow"/>) and an animated shimmer.
/// </summary>
/// <param name="Tier">Tier number (<c>0</c> = not enhanced).</param>
/// <param name="MinLevel">Lowest enhancement level in this tier.</param>
/// <param name="MaxLevel">Highest enhancement level in this tier.</param>
/// <param name="Name">Tier name shown in tooltips.</param>
/// <param name="Fill">Slot background fill.</param>
/// <param name="Accent">Badge plate and <c>+N</c> text colour.</param>
/// <param name="Glow">Innermost glow ring colour (<see cref="ColorRgba.A"/> = 0 for no glow); outer rings fade from it.</param>
/// <param name="Shimmer">Whether an animated highlight sweeps across the slot.</param>
internal readonly record struct EnhancementTierStyle(
    byte Tier,
    byte MinLevel,
    byte MaxLevel,
    string Name,
    ColorRgba Fill,
    ColorRgba Accent,
    ColorRgba Glow,
    bool Shimmer)
{
    /// <summary>Gets whether the tier draws a glow.</summary>
    public bool HasGlow => this.Glow.A > 0;
}
