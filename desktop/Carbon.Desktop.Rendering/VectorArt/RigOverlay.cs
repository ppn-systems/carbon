// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Desktop.Rendering.VectorArt;

/// <summary>
/// Presentational overlays drawn on a monster rig by <see cref="VectorRigRenderer.DrawEntityRig"/>. They are painted
/// geometry on the rig only; they never add scene lights.
/// </summary>
[Flags]
public enum RigOverlay : byte
{
    /// <summary>No overlay.</summary>
    None = 0,

    /// <summary>Night empowerment: glowing eyes and a faint body aura.</summary>
    NightGlow = 1,

    /// <summary>Elite variant: a gold crown pip above the head.</summary>
    EliteCrown = 2,
}
