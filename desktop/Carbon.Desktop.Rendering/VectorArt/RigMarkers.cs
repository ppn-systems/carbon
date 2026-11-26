// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Batching.Common;

namespace Carbon.Desktop.Rendering.VectorArt;

/// <summary>
/// Rig-local overlay anchors in design units on the head bone (<c>markers</c> in <c>.cvg.json</c>): eye positions for
/// the night eye-glow (south-facing and side-facing) and the crown anchor for the elite pip.
/// </summary>
/// <param name="Eyes">South-facing eye centres.</param>
/// <param name="EyesSide">Side-facing eye centres (east-facing; mirrored for west).</param>
/// <param name="Crown">Crown anchor (top of the head).</param>
/// <param name="EyeGlow">Eye-glow colour.</param>
public sealed record RigMarkers(Vector2[] Eyes, Vector2[] EyesSide, Vector2 Crown, ColorRgba EyeGlow)
{
    /// <summary>Gets markers with no eye anchors.</summary>
    public static RigMarkers Empty { get; } = new([], [], Vector2.Zero, new ColorRgba(248, 113, 113, 255));

    /// <summary>Gets the eye anchors for a facing (none when facing north).</summary>
    /// <param name="isSide">Whether the rig is drawn in profile.</param>
    /// <param name="isNorth">Whether the rig faces away from the camera.</param>
    /// <returns>Eye anchors in design units.</returns>
    public ReadOnlySpan<Vector2> EyesFor(bool isSide, bool isNorth) =>
        isNorth ? [] : (isSide && this.EyesSide.Length > 0 ? this.EyesSide : this.Eyes);
}
