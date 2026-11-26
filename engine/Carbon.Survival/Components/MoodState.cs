// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Survival.Components;

/// <summary>
/// Mood band derived from the 0..100 mood value, ordered from worst to best.
/// </summary>
public enum MoodState : byte
{
    /// <summary>Mood below the miserable threshold.</summary>
    Miserable = 0,

    /// <summary>Mood below the stressed threshold.</summary>
    Stressed = 1,

    /// <summary>Mood between the stressed and content thresholds.</summary>
    Neutral = 2,

    /// <summary>Mood at or above the content threshold.</summary>
    Content = 3,

    /// <summary>Mood at or above the happy threshold.</summary>
    Happy = 4,
}
