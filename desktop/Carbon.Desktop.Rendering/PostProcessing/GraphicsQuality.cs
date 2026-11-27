// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Desktop.Rendering.PostProcessing;

/// <summary>
/// The client's visual quality preset (ADR-041). Purely cosmetic: nothing gameplay-relevant reads it, and the server
/// never sees it.
/// </summary>
public enum GraphicsQuality : byte
{
    /// <summary>No offscreen targets: the world is lit by an ambient multiply quad and additive glows.</summary>
    Low = 0,

    /// <summary>A half-resolution light map multiplied over the world, with time-of-day grading and a vignette.</summary>
    Medium = 1,

    /// <summary>Medium plus light bloom gathered from the light map in the same composite pass.</summary>
    High = 2,
}
