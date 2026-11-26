// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Survival.Components;

/// <summary>
/// Rest thresholds, ordered from best to worst.
/// </summary>
public enum RestState : byte
{
    /// <summary>Rest above the tired threshold.</summary>
    Rested = 0,

    /// <summary>Rest below the tired threshold; minor mood penalty.</summary>
    Tired = 1,

    /// <summary>Rest below the very-tired threshold; larger mood penalty.</summary>
    VeryTired = 2,

    /// <summary>Rest is empty; mood penalty and movement/attack slowdown.</summary>
    Exhausted = 3,
}
